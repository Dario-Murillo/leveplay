using Leveplay.Core.Auth;
using Leveplay.Core.Config;
using Leveplay.Core.Devices;
using Leveplay.Core.Engine;
using Leveplay.Core.Library;
using SpotifyAPI.Web;

namespace Leveplay.App;

internal enum EngineState
{
    NotInstalled,
    KeyNeeded,
    Stopped,
    Running,
}

/// <summary>
/// Outcome of loading data for a view: a value, an error to show, or neither when the
/// app is shutting down.
/// </summary>
internal readonly record struct LoadResult<T>(T? Value, string? Error)
    where T : class
{
    public static LoadResult<T> Ok(T value) => new(value, null);

    public static LoadResult<T> Fail(string error) => new(null, error);

    public static LoadResult<T> Cancelled => new(null, null);
}

/// <summary>
/// Coordinates the application: sign-in, the audio engine, the connection to a device,
/// playback commands, polling and the library. It knows nothing about windows or menus.
/// The tray icon, the mini player and the main window are views that call its methods
/// and listen to its events. Everything runs on the UI thread.
/// </summary>
internal sealed class AppController : IDisposable
{
    /// <summary>Polling interval while no device is active, to save API calls.</summary>
    private const int IdlePollMilliseconds = 30_000;

    /// <summary>Spotify needs a moment before a command shows up in the player state.</summary>
    private const int CommandSettleMilliseconds = 500;

    /// <summary>How long to wait for a freshly started engine to show up as a device.</summary>
    private const int EngineStartupAttempts = 15;

    /// <summary>Environment variable used when no API key has been saved in the app.</summary>
    private const string ApiKeyVariable = "SOLOIST_API_KEY";

    /// <summary>How long an error stays on screen before polling may replace it.</summary>
    private static readonly TimeSpan ErrorHold = TimeSpan.FromSeconds(15);

    private readonly Action<Uri> _openBrowser;
    private readonly SynchronizationContext _ui = SynchronizationContext.Current!;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DpapiTokenStore _engineKeyStore = new(fileName: DpapiTokenStore.EngineApiKeyFile);
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = IdlePollMilliseconds };

    private SpotifyAuthService? _auth;
    private IPlayerGateway? _player;
    private DeviceConnector? _connector;
    private PlaybackController? _playback;
    private ILibraryGateway? _library;
    private SoloistEngine? _engine;
    private bool _engineJustStarted;
    private bool _engineAdopted;
    private DateTime _errorShownUntil = DateTime.MinValue;
    private bool _polling;

    /// <param name="openBrowser">Opens the Spotify consent page during sign-in.</param>
    public AppController(Action<Uri> openBrowser)
    {
        _openBrowser = openBrowser;
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Raised with a short lower-case status line, e.g. "signing in…".</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Raised with a title and a message when something the user should know fails.</summary>
    public event Action<string, string>? ErrorReported;

    /// <summary>Raised with every playback snapshot; null means no device is active.</summary>
    public event Action<PlaybackState?>? PlaybackChanged;

    /// <summary>Raised when what the views can offer changes: busy, signed in, engine state.</summary>
    public event Action? AvailabilityChanged;

    /// <summary>Supplied by the UI: asks the user for the Soloist API key, null if cancelled.</summary>
    public Func<string?>? AskForApiKey { get; set; }

    /// <summary>Null until the settings have been loaded successfully.</summary>
    public AppSettings? Settings { get; private set; }

    /// <summary>The latest playback snapshot, for views created after it arrived.</summary>
    public PlaybackState? Playback { get; private set; }

    /// <summary>True while a command is running; commands issued meanwhile are ignored.</summary>
    public bool IsBusy { get; private set; }

    public bool CanSignIn => _auth is not null;

    public bool IsSignedIn => _player is not null;

    public EngineState Engine
    {
        get
        {
            if (Settings is null || EngineLocator.Resolve(Settings.EnginePath) is null)
            {
                return EngineState.NotInstalled;
            }

            if (_engineAdopted || _engine is { IsRunning: true })
            {
                return EngineState.Running;
            }

            return LoadApiKey() is null ? EngineState.KeyNeeded : EngineState.Stopped;
        }
    }

    public async Task StartAsync()
    {
        SetStatus("starting…");
        try
        {
            Settings = AppSettingsLoader.Load(AppContext.BaseDirectory);
            _auth = new SpotifyAuthService(Settings, new DpapiTokenStore(), _openBrowser);
        }
        catch (SettingsException ex)
        {
            ReportError("Configuration problem", ex.Message);
            return;
        }

        AppLog.Write($"started; configured device = \"{Settings.DeviceName}\"");
        AvailabilityChanged?.Invoke();

        // Start the engine first so it has time to come online while we sign in.
        StartEngine();
        if (!await SignInAsync())
        {
            return;
        }

        // First run with Soloist installed: ask for the key right away instead of
        // leaving the user to find the menu item.
        if (Engine == EngineState.KeyNeeded)
        {
            await ChangeApiKeyAsync();
            if (Engine == EngineState.KeyNeeded)
            {
                ReportError("Soloist API key needed", "Open Audio engine → Set API key to play on this computer.");
            }

            return;
        }

        await ConnectAsync();
    }

    public async Task ReauthenticateAsync()
    {
        if (_auth is null || IsBusy)
        {
            return;
        }

        _auth.SignOut();
        _pollTimer.Stop();
        _player = null;
        _connector = null;
        _playback = null;
        _library = null;
        if (await SignInAsync())
        {
            await ConnectAsync();
        }
    }

    /// <summary>Connects to the given device, or to the configured one when none is given.</summary>
    public Task<bool> ConnectAsync(string? deviceName = null) => RunAsync("looking for device…", async ct =>
    {
        if (_connector is null || Settings is null)
        {
            return;
        }

        var name = deviceName ?? Settings.DeviceName;
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

    public Task<bool> PlayPauseAsync() => RunAsync(null, async ct =>
    {
        if (_playback is null || Settings is null)
        {
            return;
        }

        var outcome = await _playback.TogglePlayPauseAsync(Settings.DeviceName, Settings.DefaultContextUri, ct);
        if (outcome == PlayPauseOutcome.NoDevice)
        {
            ReportNoDevice();
            return;
        }

        await RefreshAfterCommandAsync(ct);
    });

    public Task<bool> SkipAsync(bool next) => RunAsync(null, async ct =>
    {
        if (_playback is null)
        {
            return;
        }

        await (next ? _playback.NextAsync(ct) : _playback.PreviousAsync(ct));
        await RefreshAfterCommandAsync(ct);
    });

    /// <summary>Plays a playlist or album, optionally starting from one of its tracks.</summary>
    public Task<bool> PlayContextAsync(string contextUri, string? trackUri) => RunAsync(null, async ct =>
    {
        if (_playback is null || Settings is null)
        {
            return;
        }

        if (!await _playback.PlayContextAsync(contextUri, Settings.DeviceName, trackUri, ct))
        {
            ReportNoDevice();
            return;
        }

        await RefreshAfterCommandAsync(ct);
    });

    public async Task SetVolumeAsync(int percent)
    {
        // A volume change must not be dropped just because a poll or command is running.
        for (var attempt = 0; IsBusy && attempt < 10; attempt++)
        {
            await Task.Delay(100);
        }

        await RunAsync(null, async ct =>
        {
            if (_playback is not null)
            {
                await _playback.SetVolumeAsync(percent, ct);
            }
        });
    }

    public Task<bool> SeekAsync(int positionMs) => RunAsync(null, async ct =>
    {
        if (_playback is null)
        {
            return;
        }

        await _playback.SeekAsync(positionMs, ct);
        await RefreshAfterCommandAsync(ct);
    });

    /// <summary>Fetches the playback state now. Failures are logged, not shown.</summary>
    public async Task RefreshAsync()
    {
        if (_player is null || IsBusy || _polling)
        {
            return;
        }

        _polling = true;
        try
        {
            Publish(await _player.GetPlaybackAsync(_shutdown.Token));
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

    // Loading data for a view stays outside RunAsync, so a slow request never blocks
    // the transport buttons.

    public Task<LoadResult<IReadOnlyList<ConnectDevice>>> GetDevicesAsync() => LoadAsync<ConnectDevice>(
        _player is null ? null : ct => _player.GetDevicesAsync(ct),
        "Sign in first", "devices", _ => "Could not load devices");

    public Task<LoadResult<IReadOnlyList<PlaylistSummary>>> GetPlaylistsAsync() => LoadAsync<PlaylistSummary>(
        _library is null ? null : ct => _library.GetPlaylistsAsync(ct),
        "Sign in to see your playlists.",
        "playlists",
        ex => ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden
            ? "Leveplay needs permission to read your playlists.\nUse \"Sign in again\" in the tray menu."
            : $"Could not load playlists.\n{ex.Message}");

    public Task<LoadResult<IReadOnlyList<TrackSummary>>> GetTracksAsync(PlaylistSummary playlist) => LoadAsync<TrackSummary>(
        _library is null ? null : ct => _library.GetPlaylistTracksAsync(playlist.Id, ct),
        "Sign in to see the tracks.",
        $"tracks of \"{playlist.Name}\"",
        ex => $"Could not load the tracks.\n{ex.Message}");

    /// <summary>Asks for a new API key and restarts the engine with it.</summary>
    public async Task ChangeApiKeyAsync()
    {
        if (IsBusy || AskForApiKey?.Invoke() is not { } apiKey)
        {
            return;
        }

        _engineKeyStore.Save(apiKey);
        AppLog.Write("engine: API key updated");
        await RestartEngineAsync();
    }

    /// <summary>Starts (or restarts) the engine on demand, e.g. after installing or updating it.</summary>
    public async Task RestartEngineAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _engine?.Stop();
        _engineAdopted = false;
        StartEngine();
        AvailabilityChanged?.Invoke();
        if (Engine == EngineState.Running && _connector is not null)
        {
            await ConnectAsync();
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _engine?.Dispose();
        _pollTimer.Dispose();
    }

    private Task<bool> SignInAsync() => RunAsync("signing in…", async ct =>
    {
        var spotify = await _auth!.SignInAsync(ct);
        _player = new SpotifyPlayerGateway(spotify.Client);
        _connector = new DeviceConnector(_player, AppLog.Write);
        _playback = new PlaybackController(_player, AppLog.Write);
        _library = new SpotifyLibraryGateway(spotify.Api, AppLog.Write);
        SetStatus("signed in");
        _pollTimer.Start();
    });

    private string? LoadApiKey()
    {
        var apiKey = _engineKeyStore.Load() ?? Environment.GetEnvironmentVariable(ApiKeyVariable);
        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    /// <summary>
    /// Launches Soloist when it is installed, either in Leveplay's engine folder or at
    /// <c>EnginePath</c>. Without it, Leveplay only controls devices that are already running.
    /// </summary>
    private void StartEngine()
    {
        if (Settings is null || EngineLocator.Resolve(Settings.EnginePath) is not { } enginePath)
        {
            AppLog.Write($"engine: not installed (looked in {EngineLocator.DefaultDirectory})");
            return;
        }

        if (LoadApiKey() is not { } apiKey)
        {
            AppLog.Write("engine: installed, but no API key has been saved yet");
            return;
        }

        // Recreated on every start: the executable may have been installed or replaced.
        _engine?.Dispose();
        _engine = new SoloistEngine(enginePath, Settings.DeviceName, AppLog.Write);
        _engine.ExitedUnexpectedly += code => _ui.Post(_ => OnEngineExited(code), null);

        try
        {
            var start = _engine.Start(apiKey);
            _engineJustStarted = start == EngineStart.Started;
            _engineAdopted = start == EngineStart.AlreadyRunning;
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
            ? " Open Audio engine → Get Soloist, replace the file, then Start."
            : "";
        ReportError("Audio engine stopped", SoloistEngine.DescribeExit(exitCode) + hint);
        AvailabilityChanged?.Invoke();
    }

    private async Task RefreshAfterCommandAsync(CancellationToken ct)
    {
        await Task.Delay(CommandSettleMilliseconds, ct);
        Publish(await _player!.GetPlaybackAsync(ct));
    }

    private void Publish(PlaybackState? state)
    {
        // Poll slowly while nothing is active; there is nothing to keep up with.
        _pollTimer.Interval = state is null
            ? IdlePollMilliseconds
            : (Settings?.PollIntervalSeconds ?? 5) * 1000;
        Playback = state;
        PlaybackChanged?.Invoke(state);

        if (DateTime.UtcNow >= _errorShownUntil)
        {
            SetStatus(PlaybackController.Describe(state));
        }
    }

    private async Task<LoadResult<IReadOnlyList<T>>> LoadAsync<T>(
        Func<CancellationToken, Task<IReadOnlyList<T>>>? load,
        string notSignedIn,
        string what,
        Func<APIException, string> describe)
    {
        if (load is null)
        {
            return LoadResult<IReadOnlyList<T>>.Fail(notSignedIn);
        }

        try
        {
            var items = await load(_shutdown.Token);
            if (items is not IReadOnlyList<ConnectDevice>)
            {
                // The device list is fetched every time the tray menu opens; not worth a line.
                AppLog.Write($"loaded {items.Count} {what}");
            }

            return LoadResult<IReadOnlyList<T>>.Ok(items);
        }
        catch (OperationCanceledException)
        {
            return LoadResult<IReadOnlyList<T>>.Cancelled;
        }
        catch (LibraryAccessException ex)
        {
            AppLog.Write($"{what} are not readable");
            return LoadResult<IReadOnlyList<T>>.Fail($"{ex.Message}\nPress Play to listen to it anyway.");
        }
        catch (APIException ex)
        {
            AppLog.Write($"loading {what} failed: {ex.Message}");
            return LoadResult<IReadOnlyList<T>>.Fail(describe(ex));
        }
        catch (HttpRequestException ex)
        {
            AppLog.Write($"loading {what} failed: {ex.Message}");
            return LoadResult<IReadOnlyList<T>>.Fail("Could not reach Spotify.");
        }
    }

    /// <summary>
    /// Runs one Spotify operation at a time, turning expected failures into a reported
    /// error instead of an unhandled exception. Returns false when the operation failed.
    /// </summary>
    private async Task<bool> RunAsync(string? status, Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        _errorShownUntil = DateTime.MinValue;
        AvailabilityChanged?.Invoke();
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
            IsBusy = false;
            AvailabilityChanged?.Invoke();
        }
    }

    private void ReportNoDevice() => ReportError(
        "Nothing to play on",
        $"No device is active and \"{Settings?.DeviceName}\" is not online. Pick one under Devices.");

    private void SetStatus(string status) => StatusChanged?.Invoke(status);

    private void ReportError(string title, string message)
    {
        AppLog.Write($"error: {title}: {message}");
        _errorShownUntil = DateTime.UtcNow + ErrorHold;
        SetStatus(title.ToLowerInvariant());
        ErrorReported?.Invoke(title, message);
    }
}
