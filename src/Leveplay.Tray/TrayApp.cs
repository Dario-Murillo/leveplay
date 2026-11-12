using System.Diagnostics;
using Leveplay.Core.Auth;
using Leveplay.Core.Config;
using Leveplay.Core.Devices;
using Leveplay.Core.Engine;
using SpotifyAPI.Web;

namespace Leveplay.Tray;

/// <summary>
/// Tray-only application: no main window, just a notification icon and its menu.
/// Signs in, makes the configured Spotify Connect device the active one, and then
/// controls playback on whichever device is active.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const int MaxTooltipLength = 127;

    /// <summary>Polling interval while no device is active, to save API calls.</summary>
    private const int IdlePollMilliseconds = 30_000;

    /// <summary>Spotify needs a moment before a command shows up in the player state.</summary>
    private const int CommandSettleMilliseconds = 500;

    /// <summary>How long to wait for a freshly started engine to show up as a device.</summary>
    private const int EngineStartupAttempts = 15;

    /// <summary>Environment variable used when no API key has been saved in the app.</summary>
    private const string ApiKeyVariable = "SOLOIST_API_KEY";

    /// <summary>Where users get Soloist; it may not be redistributed with Leveplay.</summary>
    private const string EngineDownloadPage = "https://developer.spotify.com/documentation/soloist";

    /// <summary>How long an error stays on screen before polling may replace it.</summary>
    private static readonly TimeSpan ErrorHold = TimeSpan.FromSeconds(15);

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _playPauseItem;
    private readonly ToolStripMenuItem _nextItem;
    private readonly ToolStripMenuItem _previousItem;
    private readonly ToolStripMenuItem _connectItem;
    private readonly ToolStripMenuItem _devicesItem;
    private readonly ToolStripMenuItem _signInItem;
    private readonly CancellationTokenSource _shutdown = new();
    private AppSettings? _settings;
    private SpotifyAuthService? _auth;
    private IPlayerGateway? _player;
    private DeviceConnector? _connector;
    private PlaybackController? _playback;
    private SoloistEngine? _engine;
    private bool _engineJustStarted;
    private readonly DpapiTokenStore _engineKeyStore = new(fileName: DpapiTokenStore.EngineApiKeyFile);
    private readonly SynchronizationContext _ui = SynchronizationContext.Current!;
    private readonly ToolStripMenuItem _apiKeyItem;
    private readonly ToolStripMenuItem _startEngineItem;
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = IdlePollMilliseconds };
    private DateTime _errorShownUntil = DateTime.MinValue;
    private bool _busy;
    private bool _polling;

    public TrayApp()
    {
        _statusItem = DisabledItem("Starting…");
        _playPauseItem = new ToolStripMenuItem("Play", null, async (_, _) => await PlayPauseAsync());
        _nextItem = new ToolStripMenuItem("Next", null, async (_, _) => await SkipAsync(next: true));
        _previousItem = new ToolStripMenuItem("Previous", null, async (_, _) => await SkipAsync(next: false));
        _connectItem = new ToolStripMenuItem("Connect to device", null, async (_, _) => await ConnectAsync());
        _devicesItem = new ToolStripMenuItem("Devices");
        _devicesItem.DropDownItems.Add(DisabledItem("Loading…"));
        _signInItem = new ToolStripMenuItem("Sign in again", null, async (_, _) => await ReauthenticateAsync());
        _apiKeyItem = new ToolStripMenuItem("Set Soloist API key…", null, async (_, _) => await ChangeApiKeyAsync())
        {
            Visible = false,
        };
        _startEngineItem = new ToolStripMenuItem("Start audio engine", null, async (_, _) => await RestartEngineAsync())
        {
            Visible = false,
        };

        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_playPauseItem);
        menu.Items.Add(_nextItem);
        menu.Items.Add(_previousItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_connectItem);
        menu.Items.Add(_devicesItem);
        menu.Items.Add(_signInItem);
        menu.Items.Add(_apiKeyItem);
        menu.Items.Add(_startEngineItem);
        menu.Items.Add("Get Soloist…", null, (_, _) => OpenEngineFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += async (_, _) =>
        {
            UpdateMenu();
            await RefreshDevicesAsync();
        };

        _icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        // Left click toggles play/pause; right click opens the menu.
        _icon.MouseClick += async (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                await PlayPauseAsync();
            }
        };
        _pollTimer.Tick += async (_, _) => await PollAsync();

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
        AppLog.Write($"started; configured device = \"{_settings.DeviceName}\"");

        // Start the engine first so it has time to come online while we sign in.
        StartEngine();
        if (await SignInAsync())
        {
            await ConnectAsync();
        }
    }

    /// <summary>
    /// Launches Soloist when it is installed, either in Leveplay's engine folder or at
    /// <c>EnginePath</c>. Without it, Leveplay only controls devices that are already running.
    /// </summary>
    private void StartEngine()
    {
        if (_settings is null || EngineLocator.Resolve(_settings.EnginePath) is not { } enginePath)
        {
            AppLog.Write($"engine: not installed (looked in {EngineLocator.DefaultDirectory})");
            return;
        }

        _apiKeyItem.Visible = true;
        var apiKey = _engineKeyStore.Load() ?? Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ReportError("Soloist API key missing", "Choose \"Set Soloist API key…\" in the menu.");
            return;
        }

        // Recreated on every start: the executable may have been installed or replaced.
        _engine?.Dispose();
        _engine = new SoloistEngine(enginePath, _settings.DeviceName, AppLog.Write);
        _engine.ExitedUnexpectedly += code => _ui.Post(_ => OnEngineExited(code), null);

        try
        {
            _engineJustStarted = _engine.Start(apiKey.Trim()) == EngineStart.Started;
        }
        catch (EngineException ex)
        {
            ReportError("Could not start Soloist", ex.Message);
        }
    }

    private void OnEngineExited(int exitCode)
    {
        _engineJustStarted = false;
        var hint = exitCode == SoloistEngine.ExpiredBuildExitCode
            ? " Use \"Get Soloist…\", replace the file, then \"Start audio engine\"."
            : "";
        ReportError("Audio engine stopped", SoloistEngine.DescribeExit(exitCode) + hint);
    }

    /// <summary>Starts (or restarts) the engine on demand, e.g. after installing or updating it.</summary>
    private async Task RestartEngineAsync()
    {
        if (_busy)
        {
            return;
        }

        _engine?.Stop();
        StartEngine();
        if (_engine is { IsRunning: true } && _connector is not null)
        {
            await ConnectAsync();
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

    private async Task ChangeApiKeyAsync()
    {
        if (_busy || ApiKeyPrompt.Ask() is not { } apiKey)
        {
            return;
        }

        _engineKeyStore.Save(apiKey);
        AppLog.Write("engine: API key updated");
        await RestartEngineAsync();
    }

    private async Task ReauthenticateAsync()
    {
        if (_auth is null || _busy)
        {
            return;
        }

        _auth.SignOut();
        _pollTimer.Stop();
        _player = null;
        _connector = null;
        _playback = null;
        if (await SignInAsync())
        {
            await ConnectAsync();
        }
    }

    private Task<bool> SignInAsync() => RunAsync("signing in…", async ct =>
    {
        var spotify = await _auth!.SignInAsync(ct);
        _player = new SpotifyPlayerGateway(spotify);
        _connector = new DeviceConnector(_player, AppLog.Write);
        _playback = new PlaybackController(_player, AppLog.Write);
        SetStatus("signed in");
        _pollTimer.Start();
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

        // A freshly started engine needs a few seconds to register with Spotify.
        var waitingForEngine = _engineJustStarted && deviceName is null;
        _engineJustStarted = false;
        for (var attempt = 1;
             waitingForEngine && result.Status == ConnectStatus.NotFound && attempt < EngineStartupAttempts
             && _engine is { IsRunning: true };
             attempt++)
        {
            SetStatus("starting audio engine…");
            await Task.Delay(1000, ct);
            result = await _connector.ConnectAsync(name, ct);
        }

        AppLog.Write($"connect \"{name}\": {result.Status}");
        switch (result.Status)
        {
            case ConnectStatus.Transferred:
            case ConnectStatus.AlreadyActive:
                SetStatus($"connected to {result.Device!.Name}");
                await RefreshAfterCommandAsync(ct);
                break;

            case ConnectStatus.Restricted:
                ReportError("Device cannot be controlled",
                    $"\"{name}\" is online but does not accept remote commands.");
                break;

            case ConnectStatus.NotFound:
                var available = result.AvailableDeviceNames.Count == 0
                    ? "No Spotify devices are online. Start the audio engine or open Spotify on a device."
                    : $"Online devices: {string.Join(", ", result.AvailableDeviceNames)}.";
                ReportError("Device not found", $"\"{name}\" is not online. {available}");
                break;
        }
    });

    private Task<bool> PlayPauseAsync() => RunAsync(null, async ct =>
    {
        if (_playback is null || _settings is null)
        {
            return;
        }

        var outcome = await _playback.TogglePlayPauseAsync(_settings.DeviceName, _settings.DefaultContextUri, ct);
        if (outcome == PlayPauseOutcome.NoDevice)
        {
            ReportError("Nothing to play on",
                $"No device is active and \"{_settings.DeviceName}\" is not online. Pick one under Devices.");
            return;
        }

        await RefreshAfterCommandAsync(ct);
    });

    private Task<bool> SkipAsync(bool next) => RunAsync(null, async ct =>
    {
        if (_playback is null)
        {
            return;
        }

        await (next ? _playback.NextAsync(ct) : _playback.PreviousAsync(ct));
        await RefreshAfterCommandAsync(ct);
    });

    private async Task RefreshAfterCommandAsync(CancellationToken ct)
    {
        await Task.Delay(CommandSettleMilliseconds, ct);
        ShowPlayback(await _player!.GetPlaybackAsync(ct));
    }

    /// <summary>Timer tick: refreshes the now-playing text. Failures are logged, not shown.</summary>
    private async Task PollAsync()
    {
        if (_player is null || _busy || _polling)
        {
            return;
        }

        _polling = true;
        try
        {
            ShowPlayback(await _player.GetPlaybackAsync(_shutdown.Token));
        }
        catch (OperationCanceledException)
        {
            // App is exiting.
        }
        catch (Exception ex) when (ex is APIException or HttpRequestException)
        {
            AppLog.Write($"poll failed: {ex.Message}");
            _pollTimer.Interval = IdlePollMilliseconds;
        }
        finally
        {
            _polling = false;
        }
    }

    private void ShowPlayback(PlaybackState? state)
    {
        // Poll slowly while nothing is active; there is nothing to keep up with.
        _pollTimer.Interval = state is null
            ? IdlePollMilliseconds
            : (_settings?.PollIntervalSeconds ?? 5) * 1000;
        _playPauseItem.Text = state is { IsPlaying: true } ? "Pause" : "Play";

        if (DateTime.UtcNow >= _errorShownUntil)
        {
            SetStatus(PlaybackController.Describe(state));
        }
    }

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
    private async Task<bool> RunAsync(string? status, Func<CancellationToken, Task> operation)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        _errorShownUntil = DateTime.MinValue;
        UpdateMenu();
        if (status is not null)
        {
            SetStatus(status);
        }

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
        catch (Exception ex)
        {
            AppLog.Write($"unexpected: {ex}");
            ReportError("Unexpected error", ex.Message);
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

        // Offered whenever the engine could run but is not running: just installed,
        // replaced after expiring, or stopped for any other reason.
        _startEngineItem.Visible = _settings is not null
            && _engine is not { IsRunning: true }
            && EngineLocator.Resolve(_settings.EnginePath) is not null;
        _startEngineItem.Enabled = !_busy;
        _playPauseItem.Enabled = _nextItem.Enabled = _previousItem.Enabled = _playback is not null && !_busy;
    }

    private void SetStatus(string status)
    {
        var text = $"Leveplay — {status}";
        _icon.Text = text.Length <= MaxTooltipLength ? text : text[..MaxTooltipLength];

        // Mirrored in the menu because Windows often hides balloon notifications.
        _statusItem.Text = char.ToUpperInvariant(status[0]) + status[1..];
        _statusItem.ToolTipText = null;
    }

    private void ReportError(string title, string message)
    {
        AppLog.Write($"error: {title}: {message}");
        _errorShownUntil = DateTime.UtcNow + ErrorHold;
        SetStatus(title.ToLowerInvariant());
        _statusItem.Text = $"{title}: {message}";
        _icon.ShowBalloonTip(10_000, $"Leveplay: {title}", message, ToolTipIcon.Error);
    }

    /// <summary>Loads the embedded icon at the size the tray uses on this display.</summary>
    private static Icon LoadTrayIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("Leveplay.Tray.leveplay.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, SystemInformation.SmallIconSize);
    }

    private static void OpenLog()
    {
        AppLog.Write("log opened from the tray menu");
        Process.Start(new ProcessStartInfo(AppLog.FilePath) { UseShellExecute = true });
    }

    private static void OpenBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });

    protected override void ExitThreadCore()
    {
        _shutdown.Cancel();
        _engine?.Dispose();
        _pollTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
