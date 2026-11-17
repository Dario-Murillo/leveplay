using System.Diagnostics;
using Leveplay.App.Dialogs;
using Leveplay.App.Player;
using Leveplay.App.Tray;
using Leveplay.Core.Library;

namespace Leveplay.App;

/// <summary>
/// Composition root. Creates the <see cref="AppController"/> and its views (the tray
/// icon, and the windows on demand) and connects them. It contains no application
/// logic of its own: the controller decides, the views show.
/// </summary>
internal sealed class Shell : ApplicationContext
{
    private readonly Icon _appIcon = LoadIcon();
    private readonly AppController _app;
    private readonly TrayMenu _tray;
    private MiniPlayerForm? _miniPlayer;
    private MainWindow? _mainWindow;

    public Shell()
    {
        _app = new AppController(OpenBrowser) { AskForApiKey = ApiKeyPrompt.Ask };
        _app.PlaybackChanged += state =>
        {
            _miniPlayer?.Player.ShowState(state);
            _mainWindow?.Player.ShowState(state);
        };
        _app.ErrorReported += (title, message) =>
        {
            _miniPlayer?.Player.ShowMessage($"{title}: {message}");
            _mainWindow?.Player.ShowMessage($"{title}: {message}");
        };

        _tray = new TrayMenu(_app, _appIcon) { IsMiniPlayerVisible = () => _miniPlayer is { Visible: true } };
        _tray.ToggleMiniPlayerRequested += ToggleMiniPlayer;
        _tray.OpenMainWindowRequested += OpenMainWindow;
        _tray.ExitRequested += ExitThread;

        _ = _app.StartAsync();
    }

    // Windows are created the first time they are asked for, so running tray-only never
    // pays for them.

    private void ToggleMiniPlayer()
    {
        if (_miniPlayer is null)
        {
            _miniPlayer = new MiniPlayerForm(_appIcon);
            Wire(_miniPlayer.Player);
            _miniPlayer.Player.ExtraRequested += OpenMainWindow;
        }

        _miniPlayer.Toggle();
        if (_miniPlayer.Visible)
        {
            _ = _app.RefreshAsync();
        }
    }

    private void OpenMainWindow()
    {
        if (_mainWindow is null)
        {
            var window = _mainWindow = new MainWindow(_appIcon);
            Wire(window.Player);
            window.PlayRequested += async (contextUri, trackUri) => await _app.PlayContextAsync(contextUri, trackUri);
            window.PlaylistsRequested += async () =>
            {
                var result = await _app.GetPlaylistsAsync();
                if (result.Value is { } playlists)
                {
                    window.ShowPlaylists(playlists);
                }
                else if (result.Error is { } error)
                {
                    window.ShowPlaylistsError(error);
                }
            };
            window.TracksRequested += async playlist => await ShowTracksAsync(window, playlist);
        }

        _mainWindow.Open();
        _ = _app.RefreshAsync();
    }

    private async Task ShowTracksAsync(MainWindow window, PlaylistSummary playlist)
    {
        var result = await _app.GetTracksAsync(playlist);
        if (result.Value is { } tracks)
        {
            window.ShowTracks(playlist, tracks);
        }
        else if (result.Error is { } error)
        {
            window.ShowTracksMessage(playlist, error);
        }
    }

    /// <summary>Connects a now-playing strip to the playback commands and gives it the current state.</summary>
    private void Wire(PlayerPanel player)
    {
        player.PlayPauseRequested += async () => await _app.PlayPauseAsync();
        player.NextRequested += async () => await _app.SkipAsync(next: true);
        player.PreviousRequested += async () => await _app.SkipAsync(next: false);
        player.VolumeRequested += async percent => await _app.SetVolumeAsync(percent);
        player.SeekRequested += async position => await _app.SeekAsync(position);
        player.RefreshRequested += async () => await _app.RefreshAsync();
        player.ShowState(_app.Playback);
    }

    protected override void ExitThreadCore()
    {
        _app.Dispose();
        _miniPlayer?.CloseForExit();
        _miniPlayer?.Dispose();
        _mainWindow?.CloseForExit();
        _mainWindow?.Dispose();
        _tray.Dispose();
        base.ExitThreadCore();
    }

    /// <summary>Loads the embedded icon at the size the tray uses on this display.</summary>
    private static Icon LoadIcon()
    {
        using var stream = typeof(Shell).Assembly.GetManifestResourceStream("Leveplay.App.leveplay.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, SystemInformation.SmallIconSize);
    }

    private static void OpenBrowser(Uri uri) =>
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
}
