using Leveplay.Core.Devices;
using Leveplay.Core.Library;

namespace Leveplay.App.Player;

/// <summary>
/// The full player window: playlists on the left, the tracks of the selected one on the
/// right, and the now-playing strip along the bottom. Like the mini player it holds no
/// logic; it asks for data, shows it and reports what the user picked.
/// </summary>
internal sealed class MainWindow : HideOnCloseForm
{
    private const int SidebarWidth = 290;
    private const int HeaderHeight = 76;

    private readonly Label _sidebarTitle;
    private readonly Button _reload;
    private readonly DarkList _playlists;
    private readonly Label _playlistsMessage;
    private readonly Label _listTitle;
    private readonly Label _listDetails;
    private readonly Button _playAll;
    private readonly DarkList _tracks;
    private readonly Label _tracksMessage;
    private readonly Dictionary<string, IReadOnlyList<TrackSummary>> _trackCache = [];

    private PlaylistSummary? _selected;
    private bool _playlistsLoaded;

    /// <summary>The now-playing strip; the app wires its events and feeds it state.</summary>
    public PlayerPanel Player { get; }

    /// <summary>Raised when the playlists are needed (first show, or reload).</summary>
    public event Action? PlaylistsRequested;

    /// <summary>Raised when a playlist is selected and its tracks are not cached yet.</summary>
    public event Action<PlaylistSummary>? TracksRequested;

    /// <summary>Raised with a context URI and, optionally, the track to start from.</summary>
    public event Action<string, string?>? PlayRequested;

    public MainWindow(Icon icon)
    {
        var font = Theme.BaseFont;
        Text = "Leveplay";
        Icon = icon;
        Font = font;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;

        _sidebarTitle = new Label
        {
            Text = "Playlists",
            Font = new Font(font.FontFamily, font.Size + 1, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _reload = Theme.IconButton(Theme.RefreshGlyph, "Reload playlists", 10F);
        _reload.ForeColor = Theme.Dim;
        _reload.Click += (_, _) => ReloadPlaylists();

        _playlists = new DarkList
        {
            Describe = item => item is PlaylistSummary p ? new RowText(p.Name, p.Details) : default,
        };
        _playlists.SelectedIndexChanged += (_, _) => SelectPlaylist(_playlists.SelectedItem as PlaylistSummary);
        _playlists.ItemActivated += item => PlayRequested?.Invoke(((PlaylistSummary)item).Uri, null);
        _playlistsMessage = MessageLabel();

        _listTitle = new Label
        {
            Font = new Font(font.FontFamily, font.Size + 4, FontStyle.Bold),
            AutoEllipsis = true,
            UseMnemonic = false,
            TextAlign = ContentAlignment.BottomLeft,
        };
        _listDetails = new Label
        {
            ForeColor = Theme.Dim,
            AutoEllipsis = true,
            UseMnemonic = false,
        };
        _playAll = new Button
        {
            Text = "Play",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            Font = new Font(font.FontFamily, font.Size, FontStyle.Bold),
            Visible = false,
            TabStop = false,
        };
        _playAll.FlatAppearance.BorderSize = 0;
        _playAll.Click += (_, _) =>
        {
            if (_selected is not null)
            {
                PlayRequested?.Invoke(_selected.Uri, null);
            }
        };

        _tracks = new DarkList
        {
            Describe = item => item is TrackSummary t
                ? new RowText(
                    t.Name,
                    string.Join(" · ", new[] { t.Artist, t.Album }.Where(part => !string.IsNullOrEmpty(part))),
                    PlaybackController.FormatTime(t.DurationMs))
                : default,
        };
        _tracks.ItemActivated += item =>
        {
            if (_selected is not null && item is TrackSummary { IsLocal: false } track)
            {
                PlayRequested?.Invoke(_selected.Uri, track.Uri);
            }
        };
        _tracksMessage = MessageLabel();
        _tracksMessage.Text = "Select a playlist to see its tracks.";

        Player = new PlayerPanel { Dock = DockStyle.Bottom };

        Controls.AddRange([
            _sidebarTitle, _reload, _playlists, _playlistsMessage,
            _listTitle, _listDetails, _playAll, _tracks, _tracksMessage, Player,
        ]);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.UseDarkTitleBar(this);
        Player.Height = this.Px(PlayerPanel.DesignHeight);
        MinimumSize = new Size(this.Px(820), this.Px(560));
        Size = new Size(this.Px(1100), this.Px(760));
        CenterToScreen();
    }

    /// <summary>Brings the window to the front, loading the playlists the first time.</summary>
    public void Open()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
        if (!_playlistsLoaded)
        {
            ReloadPlaylists();
        }
    }

    public void ShowPlaylists(IReadOnlyList<PlaylistSummary> playlists)
    {
        _playlistsLoaded = true;
        var previous = _selected?.Id;
        _playlists.SetItems(playlists);
        _playlistsMessage.Visible = playlists.Count == 0;
        _playlistsMessage.Text = "No playlists in your library yet.";
        _playlists.Visible = playlists.Count > 0;

        // Keep the selection across a reload when the playlist still exists.
        var index = playlists.ToList().FindIndex(p => p.Id == previous);
        if (index >= 0)
        {
            _selected = null; // Otherwise SelectPlaylist skips it and the old tracks stay.
            _playlists.SelectedIndex = index;
        }
    }

    public void ShowPlaylistsError(string message)
    {
        _playlistsMessage.Text = message;
        _playlistsMessage.Visible = true;
        _playlists.Visible = false;
    }

    /// <summary>Shows the tracks of a playlist, unless the user has moved on to another one.</summary>
    public void ShowTracks(PlaylistSummary playlist, IReadOnlyList<TrackSummary> tracks)
    {
        _trackCache[playlist.Id] = tracks;
        if (playlist.Id != _selected?.Id)
        {
            return;
        }

        if (tracks.Count == 0)
        {
            ShowTracksText("This playlist is empty.");
            return;
        }

        _tracks.SetItems(tracks);
        _tracksMessage.Visible = false;
        _tracks.Visible = true;
        _listDetails.Text = DescribeSelection(playlist, tracks.Count);
    }

    /// <summary>Shows a message instead of the tracks, e.g. when Spotify will not list them.</summary>
    public void ShowTracksMessage(PlaylistSummary playlist, string message)
    {
        if (playlist.Id == _selected?.Id)
        {
            ShowTracksText(message);
        }
    }

    private void ReloadPlaylists()
    {
        _trackCache.Clear();
        _playlistsMessage.Text = "Loading playlists…";
        _playlistsMessage.Visible = true;
        _playlists.Visible = false;
        PlaylistsRequested?.Invoke();
    }

    private void SelectPlaylist(PlaylistSummary? playlist)
    {
        if (playlist is null || playlist.Id == _selected?.Id)
        {
            return;
        }

        _selected = playlist;
        _listTitle.Text = playlist.Name;
        _listDetails.Text = DescribeSelection(playlist, playlist.TrackCount);
        _playAll.Visible = true;

        if (_trackCache.TryGetValue(playlist.Id, out var cached))
        {
            ShowTracks(playlist, cached);
            return;
        }

        ShowTracksText("Loading tracks…");
        TracksRequested?.Invoke(playlist);
    }

    private static string DescribeSelection(PlaylistSummary playlist, int? trackCount) =>
        (playlist with { TrackCount = trackCount }).Details;

    private void ShowTracksText(string text)
    {
        _tracksMessage.Text = text;
        _tracksMessage.Visible = true;
        _tracks.Visible = false;
    }

    private static Label MessageLabel() => new()
    {
        BackColor = Theme.Surface,
        ForeColor = Theme.Dim,
        TextAlign = ContentAlignment.MiddleCenter,
        UseMnemonic = false,
    };

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (!IsHandleCreated)
        {
            return;
        }

        int P(int design) => this.Px(design);
        var width = ClientSize.Width;
        var height = ClientSize.Height - Player.Height;
        var sidebar = Math.Min(P(SidebarWidth), width / 2);
        var header = P(HeaderHeight);

        // Both columns share one header height, so the two lists start on the same line.
        _sidebarTitle.Bounds = new Rectangle(P(16), 0, sidebar - P(64), header);
        _reload.Bounds = new Rectangle(sidebar - P(40), (header - P(30)) / 2, P(32), P(30));
        var playlistArea = new Rectangle(0, header, sidebar, Math.Max(0, height - header));
        _playlists.Bounds = playlistArea;
        _playlistsMessage.Bounds = playlistArea;

        var left = sidebar + P(1);
        var buttonWidth = P(96);
        _playAll.Bounds = new Rectangle(width - P(16) - buttonWidth, P(20), buttonWidth, P(36));
        var textWidth = Math.Max(0, _playAll.Left - P(16) - (left + P(16)));
        _listTitle.Bounds = new Rectangle(left + P(16), P(6), textWidth, P(38));
        _listDetails.Bounds = new Rectangle(left + P(18), P(46), textWidth, P(22));
        var trackArea = new Rectangle(left, header, Math.Max(0, width - left), Math.Max(0, height - header));
        _tracks.Bounds = trackArea;
        _tracksMessage.Bounds = trackArea;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PerformLayout();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Space belongs to the lists while one has focus, so only handle the modified keys there.
        var listFocused = _playlists.Focused || _tracks.Focused;
        if ((e.KeyCode != Keys.Space || !listFocused) && Player.HandleShortcut(e))
        {
            return;
        }

        base.OnKeyDown(e);
    }
}
