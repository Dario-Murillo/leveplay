using Leveplay.Core.Devices;

namespace Leveplay.App.Player;

/// <summary>
/// The now-playing strip: cover, track, transport buttons, volume and progress. It is
/// shared by the mini player and the main window, stretches to any width, and holds no
/// playback logic: it raises events and shows whatever state it is given.
/// </summary>
internal sealed class PlayerPanel : Panel
{
    /// <summary>Height of the strip in design units.</summary>
    public const int DesignHeight = 178;

    /// <summary>After the user moves the volume, ignore reported values for this long.</summary>
    private static readonly TimeSpan VolumeEchoGuard = TimeSpan.FromSeconds(3);

    private static readonly HttpClient ArtClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly PictureBox _art;
    private readonly Label _title;
    private readonly Label _artist;
    private readonly Label _device;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;
    private readonly Button _extra;
    private readonly Label _volumeIcon;
    private readonly SliderBar _volume;
    private readonly SliderBar _progress;
    private readonly Label _elapsed;
    private readonly Label _duration;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _volumeDebounce = new() { Interval = 250 };

    private PlaybackState? _state;
    private DateTime _stateAtUtc;
    private DateTime _volumeTouchedUtc = DateTime.MinValue;
    private DateTime _lastRefreshRequestUtc = DateTime.MinValue;
    private string? _artUrl;

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action<int>? VolumeRequested;
    public event Action<int>? SeekRequested;

    /// <summary>Raised when the strip believes its state is stale (e.g. the track ended).</summary>
    public event Action? RefreshRequested;

    /// <summary>Raised by the optional button in the top-right corner.</summary>
    public event Action? ExtraRequested;

    /// <param name="extraGlyph">Icon for an optional top-right button, or null for none.</param>
    public PlayerPanel(string? extraGlyph = null, string? extraName = null)
    {
        var font = Theme.BaseFont;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = font;
        DoubleBuffered = true;

        _art = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Raised };
        _title = new Label
        {
            Font = new Font(font.FontFamily, font.Size + 3, FontStyle.Bold),
            AutoEllipsis = true,
            UseMnemonic = false,
        };
        _artist = new Label { ForeColor = Theme.Dim, AutoEllipsis = true, UseMnemonic = false };
        _device = new Label
        {
            ForeColor = Theme.Dim,
            Font = new Font(font.FontFamily, font.Size - 1),
            AutoEllipsis = true,
            UseMnemonic = false,
        };

        _previous = Theme.IconButton(Theme.PreviousGlyph, "Previous");
        _playPause = Theme.IconButton(Theme.PlayGlyph, "Play");
        _next = Theme.IconButton(Theme.NextGlyph, "Next");
        _previous.Click += (_, _) => PreviousRequested?.Invoke();
        _playPause.Click += (_, _) => PlayPauseRequested?.Invoke();
        _next.Click += (_, _) => NextRequested?.Invoke();

        _extra = Theme.IconButton(extraGlyph ?? "", extraName ?? "", 10F);
        _extra.ForeColor = Theme.Dim;
        _extra.Visible = extraGlyph is not null;
        _extra.Click += (_, _) => ExtraRequested?.Invoke();

        _volumeIcon = new Label
        {
            Font = new Font(Theme.IconFont, 11F),
            ForeColor = Theme.Dim,
            Text = Theme.VolumeGlyph,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _volume = new SliderBar();
        _volume.ValueChanging += _ =>
        {
            _volumeTouchedUtc = DateTime.UtcNow;
            _volumeDebounce.Stop();
            _volumeDebounce.Start();
        };
        _volume.ValueCommitted += _ => SendVolume();
        _volumeDebounce.Tick += (_, _) => SendVolume();

        var small = new Font(font.FontFamily, font.Size - 1);
        _elapsed = new Label { ForeColor = Theme.Dim, Font = small, TextAlign = ContentAlignment.MiddleRight };
        _duration = new Label { ForeColor = Theme.Dim, Font = small, TextAlign = ContentAlignment.MiddleLeft };
        _progress = new SliderBar();
        _progress.ValueChanging += value =>
            _elapsed.Text = PlaybackController.FormatTime((int)(value * (_state?.DurationMs ?? 0)));
        _progress.ValueCommitted += value =>
        {
            if (_state is not { DurationMs: > 0 } state)
            {
                return;
            }

            var position = (int)(value * state.DurationMs);
            _state = state with { ProgressMs = position };
            _stateAtUtc = DateTime.UtcNow;
            SeekRequested?.Invoke(position);
        };

        Controls.AddRange([
            _art, _title, _artist, _device, _previous, _playPause, _next, _extra,
            _volumeIcon, _volume, _elapsed, _progress, _duration,
        ]);

        _tick.Tick += (_, _) => UpdateProgress();
        ShowState(null);
    }

    /// <summary>Displays a playback snapshot; null means no device is active.</summary>
    public void ShowState(PlaybackState? state)
    {
        _state = state;
        _stateAtUtc = DateTime.UtcNow;

        var hasTrack = state?.Title is not null;
        _title.Text = state?.Title ?? (state is null ? "No active device" : "Nothing playing");
        _artist.Text = state?.Artist ?? "";
        _device.ForeColor = Theme.Dim;
        _device.Text = state is null ? "Press play to start on this computer" : $"Playing on {state.DeviceName}";

        _playPause.Text = state is { IsPlaying: true } ? Theme.PauseGlyph : Theme.PlayGlyph;
        _previous.Enabled = _next.Enabled = hasTrack;
        _progress.Enabled = state is { DurationMs: > 0 };
        _duration.Text = hasTrack ? PlaybackController.FormatTime(state!.DurationMs) : "";

        var volumeKnown = state?.VolumePercent is not null;
        _volume.Enabled = volumeKnown;
        if (volumeKnown && DateTime.UtcNow - _volumeTouchedUtc > VolumeEchoGuard)
        {
            _volume.Value = state!.VolumePercent!.Value / 100d;
        }

        UpdateProgress();
        _ = LoadArtAsync(state?.ArtUrl);
    }

    /// <summary>
    /// Handles the playback shortcuts for the hosting window: Space plays or pauses,
    /// Ctrl+Right and Ctrl+Left skip. Returns true when the key was one of them.
    /// </summary>
    public bool HandleShortcut(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Space:
                PlayPauseRequested?.Invoke();
                break;
            case Keys.Right when e.Control:
                NextRequested?.Invoke();
                break;
            case Keys.Left when e.Control:
                PreviousRequested?.Invoke();
                break;
            default:
                return false;
        }

        e.Handled = e.SuppressKeyPress = true;
        return true;
    }

    /// <summary>Shows an error in place of the device line until the next state arrives.</summary>
    public void ShowMessage(string message)
    {
        _device.ForeColor = Theme.Warning;
        _device.Text = message;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        int P(int design) => this.Px(design);
        var width = ClientSize.Width;
        var right = width - P(16);

        _art.Bounds = new Rectangle(P(16), P(16), P(104), P(104));

        var textLeft = P(134);
        var textRight = _extra.Visible ? right - P(38) : right;
        _extra.Bounds = new Rectangle(right - P(32), P(11), P(32), P(30));
        _title.Bounds = new Rectangle(textLeft, P(12), Math.Max(0, textRight - textLeft), P(30));
        _artist.Bounds = new Rectangle(textLeft, P(42), Math.Max(0, right - textLeft), P(22));
        _device.Bounds = new Rectangle(textLeft, P(64), Math.Max(0, right - textLeft), P(20));

        _previous.Bounds = new Rectangle(P(132), P(88), P(36), P(32));
        _playPause.Bounds = new Rectangle(P(172), P(88), P(36), P(32));
        _next.Bounds = new Rectangle(P(212), P(88), P(36), P(32));

        var volumeWidth = P(120);
        _volume.Bounds = new Rectangle(right - volumeWidth, P(96), volumeWidth, P(16));
        _volumeIcon.Bounds = new Rectangle(_volume.Left - P(28), P(88), P(26), P(32));

        _elapsed.Bounds = new Rectangle(P(12), P(144), P(44), P(20));
        _duration.Bounds = new Rectangle(width - P(56), P(144), P(44), P(20));
        _progress.Bounds = new Rectangle(P(60), P(146), Math.Max(P(20), width - P(120)), P(16));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Hairlines: one above the strip and one above the progress row.
        using var pen = new Pen(Theme.Hover);
        e.Graphics.DrawLine(pen, 0, 0, ClientSize.Width, 0);
        var y = this.Px(134);
        e.Graphics.DrawLine(pen, this.Px(16), y, ClientSize.Width - this.Px(16), y);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        // No timers while hidden: the strip should cost nothing when it is not shown.
        _tick.Enabled = Visible;
        base.OnVisibleChanged(e);
    }

    /// <summary>
    /// Advances the bar between polls so it moves smoothly without extra API calls, and
    /// asks for a refresh once the track should have ended.
    /// </summary>
    private void UpdateProgress()
    {
        if (_state is not { DurationMs: > 0 } state)
        {
            _progress.Value = 0;
            _elapsed.Text = "";
            return;
        }

        var position = state.ProgressMs;
        if (state.IsPlaying)
        {
            position += (int)(DateTime.UtcNow - _stateAtUtc).TotalMilliseconds;
        }

        if (position >= state.DurationMs + 750 && DateTime.UtcNow - _lastRefreshRequestUtc > TimeSpan.FromSeconds(2))
        {
            _lastRefreshRequestUtc = DateTime.UtcNow;
            RefreshRequested?.Invoke();
        }

        position = Math.Min(position, state.DurationMs);
        if (!_progress.IsDragging)
        {
            _progress.Value = (double)position / state.DurationMs;
            _elapsed.Text = PlaybackController.FormatTime(position);
        }
    }

    private void SendVolume()
    {
        _volumeDebounce.Stop();
        _volumeTouchedUtc = DateTime.UtcNow;
        VolumeRequested?.Invoke((int)Math.Round(_volume.Value * 100));
    }

    private async Task LoadArtAsync(string? url)
    {
        if (url == _artUrl)
        {
            return;
        }

        _artUrl = url;
        Image? image = null;
        if (url is not null)
        {
            try
            {
                var bytes = await ArtClient.GetByteArrayAsync(url);
                using var stream = new MemoryStream(bytes);
                using var decoded = Image.FromStream(stream);
                image = new Bitmap(decoded); // Detach from the stream so it can be disposed.
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
            {
                // No cover is fine; the placeholder stays.
            }
        }

        // A newer track may have arrived while this one was downloading.
        if (url != _artUrl || IsDisposed)
        {
            image?.Dispose();
            return;
        }

        var old = _art.Image;
        _art.Image = image;
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Dispose();
            _volumeDebounce.Dispose();
            _art.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
