using System.Diagnostics;
using Leveplay.Core.Engine;

namespace Leveplay.App.Tray;

/// <summary>
/// The notification icon and its menu. It is one of the views of the application: it
/// shows what the <see cref="AppController"/> reports and forwards clicks to it.
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private const int MaxTooltipLength = 127;

    /// <summary>Where users get Soloist; it may not be redistributed with Leveplay.</summary>
    private const string EngineDownloadPage = "https://developer.spotify.com/documentation/soloist";

    private readonly AppController _app;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _playerItem;
    private readonly ToolStripMenuItem _playPauseItem;
    private readonly ToolStripMenuItem _nextItem;
    private readonly ToolStripMenuItem _previousItem;
    private readonly ToolStripMenuItem _connectItem;
    private readonly ToolStripMenuItem _devicesItem;
    private readonly ToolStripMenuItem _engineItem;
    private readonly ToolStripMenuItem _getEngineItem;
    private readonly ToolStripMenuItem _apiKeyItem;
    private readonly ToolStripMenuItem _startEngineItem;
    private readonly ToolStripMenuItem _expiryItem;
    private readonly ToolStripMenuItem _signInItem;

    public event Action? OpenMainWindowRequested;

    public event Action? ToggleMiniPlayerRequested;

    public event Action? ExitRequested;

    /// <summary>Supplied by the shell so the menu can label the mini player entry.</summary>
    public Func<bool> IsMiniPlayerVisible { get; set; } = () => false;

    public TrayMenu(AppController app, Icon icon)
    {
        _app = app;

        _statusItem = DisabledItem("Starting…");
        _playerItem = new ToolStripMenuItem("Show mini player", null, (_, _) => ToggleMiniPlayerRequested?.Invoke());
        _playPauseItem = new ToolStripMenuItem("Play", null, async (_, _) => await _app.PlayPauseAsync());
        _nextItem = new ToolStripMenuItem("Next", null, async (_, _) => await _app.SkipAsync(next: true));
        _previousItem = new ToolStripMenuItem("Previous", null, async (_, _) => await _app.SkipAsync(next: false));
        _connectItem = new ToolStripMenuItem("Connect to device", null, async (_, _) => await _app.ConnectAsync());
        _devicesItem = new ToolStripMenuItem("Devices");
        _devicesItem.DropDownItems.Add(DisabledItem("Loading…"));
        _signInItem = new ToolStripMenuItem("Sign in again", null, async (_, _) => await _app.ReauthenticateAsync());

        // The engine submenu reads as a three-step checklist: each step is ticked once
        // done and the next one is the only thing left enabled.
        _getEngineItem = new ToolStripMenuItem("1. Get Soloist…", null, (_, _) => OpenEngineFolder());
        _apiKeyItem = new ToolStripMenuItem("2. Set API key…", null, async (_, _) => await _app.ChangeApiKeyAsync());
        _startEngineItem = new ToolStripMenuItem("3. Start", null, async (_, _) => await _app.RestartEngineAsync());
        _engineItem = new ToolStripMenuItem("Audio engine");
        _expiryItem = DisabledItem("");
        _engineItem.DropDownItems.AddRange([_getEngineItem, _apiKeyItem, _startEngineItem, _expiryItem]);

        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Leveplay", null, (_, _) => OpenMainWindowRequested?.Invoke());
        menu.Items.Add(_playerItem);
        menu.Items.Add(_playPauseItem);
        menu.Items.Add(_nextItem);
        menu.Items.Add(_previousItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_connectItem);
        menu.Items.Add(_devicesItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_engineItem);
        menu.Items.Add(_signInItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        menu.Opening += async (_, _) =>
        {
            Refresh();
            await RefreshDevicesAsync();
        };

        _icon = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };

        // Left click shows or hides the mini player; right click opens the menu.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleMiniPlayerRequested?.Invoke();
            }
        };

        _app.StatusChanged += ShowStatus;
        _app.ErrorReported += ShowError;
        _app.PlaybackChanged += state => _playPauseItem.Text = state is { IsPlaying: true } ? "Pause" : "Play";
        _app.AvailabilityChanged += Refresh;

        ShowStatus("starting…");
        Refresh();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void ShowStatus(string status)
    {
        var text = $"Leveplay — {status}";
        _icon.Text = text.Length <= MaxTooltipLength ? text : text[..MaxTooltipLength];

        // Mirrored in the menu because Windows often hides balloon notifications.
        _statusItem.Text = char.ToUpperInvariant(status[0]) + status[1..];
    }

    private void ShowError(string title, string message)
    {
        _statusItem.Text = $"{title}: {message}";
        _icon.ShowBalloonTip(10_000, $"Leveplay: {title}", message, ToolTipIcon.Error);
    }

    /// <summary>Brings every entry in line with what the controller can do right now.</summary>
    private void Refresh()
    {
        var busy = _app.IsBusy;
        _signInItem.Enabled = _app.CanSignIn && !busy;
        _connectItem.Enabled = _app.IsSignedIn && !busy;
        _playPauseItem.Enabled = _nextItem.Enabled = _previousItem.Enabled = _app.IsSignedIn && !busy;
        if (_app.Settings is { } settings)
        {
            _connectItem.Text = $"Connect to \"{settings.DeviceName}\"";
        }

        _playerItem.Text = IsMiniPlayerVisible() ? "Hide mini player" : "Show mini player";

        var engine = _app.Engine;
        _engineItem.Text = engine switch
        {
            EngineState.NotInstalled => "Audio engine (not installed)",
            EngineState.KeyNeeded => "Audio engine (API key needed)",
            EngineState.Stopped => "Audio engine (stopped)",
            _ => "Audio engine (running)",
        };
        _getEngineItem.Checked = engine != EngineState.NotInstalled;
        _getEngineItem.Text = engine == EngineState.NotInstalled ? "1. Get Soloist…" : "1. Soloist installed (update…)";
        _apiKeyItem.Checked = engine is EngineState.Stopped or EngineState.Running;
        _apiKeyItem.Text = _apiKeyItem.Checked ? "2. API key saved (change…)" : "2. Set API key…";
        _apiKeyItem.Enabled = engine != EngineState.NotInstalled && !busy;
        _startEngineItem.Checked = engine == EngineState.Running;
        _startEngineItem.Text = engine == EngineState.Running ? "3. Running (restart)" : "3. Start";
        _startEngineItem.Enabled = engine is EngineState.Stopped or EngineState.Running && !busy;

        // Soloist builds stop working 90 days after they were built.
        _expiryItem.Visible = _app.EngineExpiresOn is not null && engine == EngineState.Running;
        if (_app.EngineExpiresOn is { } expiresOn)
        {
            _expiryItem.Text = $"This build expires {AppController.DescribeExpiry(expiresOn)}";
        }
    }

    /// <summary>
    /// Rebuilds the Devices submenu each time the menu opens, so it always shows what is
    /// online right now. The active device is checked; clicking another one switches to it.
    /// </summary>
    private async Task RefreshDevicesAsync()
    {
        var result = await _app.GetDevicesAsync();
        var items = _devicesItem.DropDownItems;
        if (result.Value is not { } devices)
        {
            if (result.Error is not null)
            {
                items.Clear();
                items.Add(DisabledItem(result.Error));
            }

            return;
        }

        items.Clear();
        foreach (var device in devices)
        {
            var (id, name) = (device.Id, device.Name);
            items.Add(new ToolStripMenuItem($"{name} ({device.Type})", null, async (_, _) => await _app.ConnectAsync(name, id))
            {
                Checked = device.IsActive,
                Enabled = device.IsControllable,
            });
        }

        if (devices.Count == 0)
        {
            items.Add(DisabledItem("No devices online"));
        }
    }

    /// <summary>
    /// Opens the official download page together with the folder the file goes into.
    /// Leveplay does not download Soloist itself: Spotify asks that users get it from them.
    /// </summary>
    private static void OpenEngineFolder()
    {
        Directory.CreateDirectory(EngineLocator.DefaultDirectory);
        AppLog.Write($"engine: opening {EngineLocator.DefaultDirectory} and the download page");
        Process.Start(new ProcessStartInfo(EngineLocator.DefaultDirectory) { UseShellExecute = true });
        Process.Start(new ProcessStartInfo(EngineDownloadPage) { UseShellExecute = true });
    }

    private static void OpenLog()
    {
        AppLog.Write("log opened from the tray menu");
        Process.Start(new ProcessStartInfo(AppLog.FilePath) { UseShellExecute = true });
    }

    private static ToolStripMenuItem DisabledItem(string text) => new(text) { Enabled = false };
}
