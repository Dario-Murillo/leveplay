using System.Diagnostics;
using Leveplay.Core.Auth;
using Leveplay.Core.Config;
using Leveplay.Core.Devices;
using SpotifyAPI.Web;

namespace Leveplay.Tray;

/// <summary>
/// Tray-only application: no main window, just a notification icon and its menu.
/// Signs in, then makes the configured Spotify Connect device the active one.
/// Playback controls come next.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const int MaxTooltipLength = 127;

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _connectItem;
    private readonly ToolStripMenuItem _devicesItem;
    private readonly ToolStripMenuItem _signInItem;
    private readonly CancellationTokenSource _shutdown = new();
    private AppSettings? _settings;
    private SpotifyAuthService? _auth;
    private IPlayerGateway? _player;
    private DeviceConnector? _connector;
    private bool _busy;

    public TrayApp()
    {
        _connectItem = new ToolStripMenuItem("Connect to device", null, async (_, _) => await ConnectAsync());
        _devicesItem = new ToolStripMenuItem("Devices");
        _devicesItem.DropDownItems.Add(DisabledItem("Loading…"));
        _signInItem = new ToolStripMenuItem("Sign in again", null, async (_, _) => await ReauthenticateAsync());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_connectItem);
        menu.Items.Add(_devicesItem);
        menu.Items.Add(_signInItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += async (_, _) => await RefreshDevicesAsync();

        _icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        SetStatus("starting…");
        UpdateMenu();

        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            _settings = AppSettingsLoader.Load(AppContext.BaseDirectory);
            _auth = new SpotifyAuthService(_settings, new DpapiTokenStore(), OpenBrowser);
        }
        catch (SettingsException ex)
        {
            ReportError("Configuration problem", ex.Message);
            return;
        }

        _connectItem.Text = $"Connect to \"{_settings.DeviceName}\"";
        if (await SignInAsync())
        {
            await ConnectAsync();
        }
    }

    private async Task ReauthenticateAsync()
    {
        if (_auth is null || _busy)
        {
            return;
        }

        _auth.SignOut();
        _player = null;
        _connector = null;
        if (await SignInAsync())
        {
            await ConnectAsync();
        }
    }

    private Task<bool> SignInAsync() => RunAsync("signing in…", async ct =>
    {
        var spotify = await _auth!.SignInAsync(ct);
        _player = new SpotifyPlayerGateway(spotify);
        _connector = new DeviceConnector(_player);
        SetStatus("signed in");
    });

    /// <summary>Connects to the given device, or to the configured one when none is given.</summary>
    private Task<bool> ConnectAsync(string? deviceName = null) => RunAsync("looking for device…", async ct =>
    {
        if (_connector is null || _settings is null)
        {
            return;
        }

        var name = deviceName ?? _settings.DeviceName;
        var result = await _connector.ConnectAsync(name, ct);
        switch (result.Status)
        {
            case ConnectStatus.Transferred:
            case ConnectStatus.AlreadyActive:
                SetStatus($"connected to {result.Device!.Name}");
                break;

            case ConnectStatus.Restricted:
                ReportError("Device cannot be controlled",
                    $"\"{name}\" is online but does not accept remote commands.");
                break;

            case ConnectStatus.NotFound:
                var available = result.AvailableDeviceNames.Count == 0
                    ? "No Spotify devices are online. Start spotifyd or open Spotify on a device."
                    : $"Online devices: {string.Join(", ", result.AvailableDeviceNames)}.";
                ReportError("Device not found", $"\"{name}\" is not online. {available}");
                break;
        }
    });

    /// <summary>
    /// Rebuilds the Devices submenu each time the menu opens, so it always shows what is
    /// online right now. The active device is checked; clicking another one switches to it.
    /// </summary>
    private async Task RefreshDevicesAsync()
    {
        var items = _devicesItem.DropDownItems;
        if (_player is null)
        {
            items.Clear();
            items.Add(DisabledItem("Sign in first"));
            return;
        }

        try
        {
            var devices = await _player.GetDevicesAsync(_shutdown.Token);
            items.Clear();
            foreach (var device in devices)
            {
                var name = device.Name;
                var item = new ToolStripMenuItem($"{name} ({device.Type})", null, async (_, _) => await ConnectAsync(name))
                {
                    Checked = device.IsActive,
                    Enabled = !device.IsRestricted && !string.IsNullOrEmpty(device.Id),
                };
                items.Add(item);
            }

            if (devices.Count == 0)
            {
                items.Add(DisabledItem("No devices online"));
            }
        }
        catch (OperationCanceledException)
        {
            // App is exiting.
        }
        catch (Exception ex) when (ex is APIException or HttpRequestException)
        {
            items.Clear();
            items.Add(DisabledItem("Could not load devices"));
        }
    }

    private static ToolStripMenuItem DisabledItem(string text) => new(text) { Enabled = false };

    /// <summary>
    /// Runs one Spotify operation at a time, turning expected failures into a balloon
    /// instead of an unhandled exception. Returns false when the operation failed.
    /// </summary>
    private async Task<bool> RunAsync(string status, Func<CancellationToken, Task> operation)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        UpdateMenu();
        SetStatus(status);
        try
        {
            await operation(_shutdown.Token);
            return true;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return false; // App is exiting.
        }
        catch (AuthException ex)
        {
            ReportError("Sign-in failed", ex.Message);
            return false;
        }
        catch (APIException ex)
        {
            ReportError("Spotify rejected the request", ex.Message);
            return false;
        }
        catch (HttpRequestException ex)
        {
            ReportError("Could not reach Spotify", ex.Message);
            return false;
        }
        finally
        {
            _busy = false;
            UpdateMenu();
        }
    }

    private void UpdateMenu()
    {
        _signInItem.Enabled = _auth is not null && !_busy;
        _connectItem.Enabled = _connector is not null && !_busy;
    }

    private void SetStatus(string status)
    {
        var text = $"Leveplay — {status}";
        _icon.Text = text.Length <= MaxTooltipLength ? text : text[..MaxTooltipLength];
    }

    private void ReportError(string title, string message)
    {
        SetStatus(title.ToLowerInvariant());
        _icon.ShowBalloonTip(10_000, $"Leveplay: {title}", message, ToolTipIcon.Error);
    }

    /// <summary>Loads the embedded icon at the size the tray uses on this display.</summary>
    private static Icon LoadTrayIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("Leveplay.Tray.leveplay.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, SystemInformation.SmallIconSize);
    }

    private static void OpenBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

    protected override void ExitThreadCore()
    {
        _shutdown.Cancel();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
