using System.Diagnostics;
using Leveplay.Core.Auth;
using Leveplay.Core.Config;
using SpotifyAPI.Web;

namespace Leveplay.Tray;

/// <summary>
/// Tray-only application: no main window, just a notification icon and its menu.
/// Milestone 1 covers sign-in only; device and playback controls come next.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const int MaxTooltipLength = 127;

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _signInItem;
    private readonly CancellationTokenSource _shutdown = new();
    private SpotifyAuthService? _auth;
    private SpotifyClient? _spotify;

    public TrayApp()
    {
        _signInItem = new ToolStripMenuItem("Sign in again", null, async (_, _) => await ReauthenticateAsync());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_signInItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _icon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        SetStatus("starting…");

        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            var settings = AppSettingsLoader.Load(AppContext.BaseDirectory);
            _auth = new SpotifyAuthService(settings, new DpapiTokenStore(), OpenBrowser);
        }
        catch (SettingsException ex)
        {
            ReportError("Configuration problem", ex.Message);
            return;
        }

        await SignInAsync();
    }

    private async Task ReauthenticateAsync()
    {
        if (_auth is null)
        {
            return;
        }

        _auth.SignOut();
        await SignInAsync();
    }

    private async Task SignInAsync()
    {
        _signInItem.Enabled = false;
        SetStatus("signing in…");
        try
        {
            _spotify = await _auth!.SignInAsync(_shutdown.Token);
            var profile = await _spotify.UserProfile.Current(_shutdown.Token);
            SetStatus($"signed in as {profile.DisplayName ?? profile.Id}");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // App is exiting.
        }
        catch (AuthException ex)
        {
            ReportError("Sign-in failed", ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or APIException)
        {
            ReportError("Could not reach Spotify", ex.Message);
        }
        finally
        {
            _signInItem.Enabled = _auth is not null;
        }
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
