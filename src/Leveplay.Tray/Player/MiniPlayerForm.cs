using System.Drawing.Drawing2D;
using Leveplay.Core.Devices;

namespace Leveplay.Tray.Player;

/// <summary>
/// Small optional window with the cover, track, progress, volume and transport buttons.
/// It holds no playback logic: it raises events and shows whatever state it is given.
/// Closing it only hides it; the tray icon keeps the app alive.
/// </summary>
internal sealed class MiniPlayerForm : Form
{
    private static readonly Color Background = Color.FromArgb(23, 23, 28);
    private static readonly Color Surface = Color.FromArgb(38, 38, 46);
    private static readonly Color Hover = Color.FromArgb(52, 52, 61);
    private static readonly Color TextColor = Color.FromArgb(242, 242, 245);
    private static readonly Color DimColor = Color.FromArgb(154, 154, 165);
    private static readonly Color WarningColor = Color.FromArgb(255, 176, 96);

    // Segoe MDL2 Assets ships with Windows 10 and 11.
    private const string IconFont = "Segoe MDL2 Assets";
    private const string PreviousGlyph = "";
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string NextGlyph = "";
    private const string VolumeGlyph = "";

    /// <summary>After the user moves the volume, ignore reported values for this long.</summary>
    private static readonly TimeSpan VolumeEchoGuard = TimeSpan.FromSeconds(3);

    private static readonly HttpClient ArtClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly PictureBox _art;
    private readonly Label _title;
    private readonly Label _artist;
    private readonly Label _device;
    private readonly Button _playPause;
    private readonly Button _previous;
    private readonly Button _next;
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
    private bool _allowClose;

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action<int>? VolumeRequested;
    public event Action<int>? SeekRequested;

    /// <summary>Raised when the window believes its state is stale (e.g. the track ended).</summary>
    public event Action? RefreshRequested;

    public MiniPlayerForm(Icon icon)
    {
        Text = "Leveplay";
        Icon = icon;
        Font = SystemFonts.MessageBoxFont ?? Font;
        BackColor = Background;
        ForeColor = TextColor;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 178);

        _art = new PictureBox
        {
            Bounds = new Rectangle(16, 16, 104, 104),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Surface,
        };

        _title = new Label
        {
            Bounds = new Rectangle(134, 12, 290, 30),
            Font = new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold),
            AutoEllipsis = true,
            UseMnemonic = false,
            Text = "Nothing playing",
        };
        _artist = new Label
        {
            Bounds = new Rectangle(134, 42, 290, 22),
            ForeColor = DimColor,
            AutoEllipsis = true,
            UseMnemonic = false,
        };
        _device = new Label
        {
            Bounds = new Rectangle(134, 64, 290, 20),
            ForeColor = DimColor,
            Font = new Font(Font.FontFamily, Font.Size - 1),
            AutoEllipsis = true,
            UseMnemonic = false,
        };

        _previous = TransportButton(PreviousGlyph, new Rectangle(132, 88, 36, 32), "Previous");
        _playPause = TransportButton(PlayGlyph, new Rectangle(172, 88, 36, 32), "Play");
        _next = TransportButton(NextGlyph, new Rectangle(212, 88, 36, 32), "Next");
        _previous.Click += (_, _) => PreviousRequested?.Invoke();
        _playPause.Click += (_, _) => PlayPauseRequested?.Invoke();
        _next.Click += (_, _) => NextRequested?.Invoke();

        var volumeIcon = new Label
        {
            Bounds = new Rectangle(276, 88, 26, 32),
            Font = new Font(IconFont, 11F),
            ForeColor = DimColor,
            Text = VolumeGlyph,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _volume = new SliderBar { Bounds = new Rectangle(304, 96, 120, 16) };
        _volume.ValueChanging += _ =>
        {
            _volumeTouchedUtc = DateTime.UtcNow;
            _volumeDebounce.Stop();
            _volumeDebounce.Start();
        };
        _volume.ValueCommitted += _ => SendVolume();
        _volumeDebounce.Tick += (_, _) => SendVolume();

        _elapsed = TimeLabel(new Rectangle(12, 144, 44, 20), ContentAlignment.MiddleRight);
        _progress = new SliderBar { Bounds = new Rectangle(60, 146, 320, 16) };
        _duration = TimeLabel(new Rectangle(384, 144, 44, 20), ContentAlignment.MiddleLeft);
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
            _art, _title, _artist, _device, _previous, _playPause, _next,
            volumeIcon, _volume, _elapsed, _progress, _duration,
        ]);

        _tick.Tick += (_, _) => UpdateProgress();
        ShowState(null);
    }

    /// <summary>Lets the window really close when the app is exiting.</summary>
    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Shows the window in the corner next to the tray, or hides it if visible.</summary>
    public void Toggle()
    {
        if (Visible)
        {
            Hide();
            return;
        }

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        Activate();
        RefreshRequested?.Invoke();
    }

    /// <summary>Displays a playback snapshot; null means no device is active.</summary>
    public void ShowState(PlaybackState? state)
    {
        _state = state;
        _stateAtUtc = DateTime.UtcNow;

        var hasTrack = state?.Title is not null;
        _title.Text = state?.Title ?? (state is null ? "No active device" : "Nothing playing");
        _artist.Text = state?.Artist ?? "";
        _device.ForeColor = DimColor;
        _device.Text = state is null
            ? "Press play to start on this computer"
            : $"Playing on {state.DeviceName}";

        _playPause.Text = state is { IsPlaying: true } ? PauseGlyph : PlayGlyph;
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
        if (Visible && !_tick.Enabled)
        {
            _tick.Start();
        }
    }

    /// <summary>Shows an error in place of the device line until the next state arrives.</summary>
    public void ShowMessage(string message)
    {
        _device.ForeColor = WarningColor;
        _device.Text = message;
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

    private Button TransportButton(string glyph, Rectangle bounds, string accessibleName)
    {
        var button = new Button
        {
            Bounds = bounds,
            Text = glyph,
            Font = new Font(IconFont, 11F),
            FlatStyle = FlatStyle.Flat,
            BackColor = Background,
            ForeColor = TextColor,
            AccessibleName = accessibleName,
            TabStop = false,
            UseMnemonic = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Hover;
        button.FlatAppearance.MouseDownBackColor = Surface;
        return button;
    }

    private Label TimeLabel(Rectangle bounds, ContentAlignment alignment) => new()
    {
        Bounds = bounds,
        ForeColor = DimColor,
        Font = new Font(Font.FontFamily, Font.Size - 1),
        TextAlign = alignment,
    };

    protected override void OnVisibleChanged(EventArgs e)
    {
        // No timers while hidden: the window should cost nothing when it is not shown.
        _tick.Enabled = Visible;
        base.OnVisibleChanged(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Hide();
                break;
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
                base.OnKeyDown(e);
                return;
        }

        e.Handled = e.SuppressKeyPress = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Hairline above the progress row.
        using var pen = new Pen(Hover);
        var y = LogicalToDeviceUnits(134);
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.DrawLine(pen, LogicalToDeviceUnits(16), y, ClientSize.Width - LogicalToDeviceUnits(16), y);
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
