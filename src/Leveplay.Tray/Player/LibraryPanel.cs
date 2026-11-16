using System.Runtime.InteropServices;
using Leveplay.Core.Library;

namespace Leveplay.Tray.Player;

/// <summary>
/// The expandable part of the mini player: a list of the user's playlists. Like the
/// rest of the window it holds no logic; it asks for data and reports the choice.
/// </summary>
internal sealed class LibraryPanel : Panel
{
    private static readonly Color Background = Color.FromArgb(23, 23, 28);
    private static readonly Color Surface = Color.FromArgb(30, 30, 37);
    private static readonly Color HoverColor = Color.FromArgb(44, 44, 54);
    private static readonly Color SelectedColor = Color.FromArgb(66, 52, 130);
    private static readonly Color TextColor = Color.FromArgb(242, 242, 245);
    private static readonly Color DimColor = Color.FromArgb(154, 154, 165);

    private const string RefreshGlyph = "\uE72C";
    private const int RowHeight = 46;

    private readonly ListBox _list;
    private readonly Label _message;
    private readonly Font _nameFont;
    private readonly Font _detailFont;
    private int _hoverIndex = -1;

    /// <summary>Raised when a playlist is double-clicked or chosen with Enter.</summary>
    public event Action<PlaylistSummary>? PlaylistChosen;

    public event Action? ReloadRequested;

    /// <summary>True once playlists have been shown, so reopening does not reload them.</summary>
    public bool HasData { get; private set; }

    public LibraryPanel(Font baseFont)
    {
        BackColor = Background;
        Size = new Size(440, 286);
        _nameFont = new Font(baseFont.FontFamily, baseFont.Size + 0.5f);
        _detailFont = new Font(baseFont.FontFamily, baseFont.Size - 1);

        var header = new Label
        {
            Bounds = new Rectangle(16, 6, 300, 32),
            Font = new Font(baseFont.FontFamily, baseFont.Size + 1, FontStyle.Bold),
            ForeColor = TextColor,
            Text = "Your playlists",
        };
        var refresh = new Button
        {
            Bounds = new Rectangle(392, 6, 32, 30),
            Text = RefreshGlyph,
            Font = new Font("Segoe MDL2 Assets", 10F),
            FlatStyle = FlatStyle.Flat,
            BackColor = Background,
            ForeColor = DimColor,
            AccessibleName = "Reload playlists",
            TabStop = false,
            UseMnemonic = false,
        };
        refresh.FlatAppearance.BorderSize = 0;
        refresh.FlatAppearance.MouseOverBackColor = HoverColor;
        refresh.Click += (_, _) => Reload();

        _list = new ListBox
        {
            Bounds = new Rectangle(16, 42, 408, 230),
            BorderStyle = BorderStyle.None,
            BackColor = Surface,
            ForeColor = TextColor,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = RowHeight,
            IntegralHeight = false,
            Visible = false,
        };
        _list.DrawItem += DrawRow;
        _list.DoubleClick += (_, _) => ChooseSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                ChooseSelected();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        _list.MouseMove += (_, e) => SetHover(_list.IndexFromPoint(e.Location));
        _list.MouseLeave += (_, _) => SetHover(-1);
        _list.HandleCreated += (_, _) =>
        {
            // Owner-drawn rows are not scaled by the form, and the stock scrollbar is light.
            _list.ItemHeight = this.Px(RowHeight);
            _ = SetWindowTheme(_list.Handle, "DarkMode_Explorer", null);
        };

        _message = new Label
        {
            Bounds = _list.Bounds,
            BackColor = Surface,
            ForeColor = DimColor,
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
        };

        Controls.AddRange([header, refresh, _list, _message]);
    }

    public void ShowLoading() => ShowMessageText("Loading playlists…");

    public void ShowError(string message) => ShowMessageText(message);

    public void ShowPlaylists(IReadOnlyList<PlaylistSummary> playlists)
    {
        HasData = true;
        if (playlists.Count == 0)
        {
            ShowMessageText("No playlists in your library yet.");
            return;
        }

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var playlist in playlists)
        {
            _list.Items.Add(playlist);
        }

        _list.EndUpdate();
        _message.Visible = false;
        _list.Visible = true;
    }

    private void Reload()
    {
        ShowLoading();
        ReloadRequested?.Invoke();
    }

    private void ShowMessageText(string text)
    {
        _message.Text = text;
        _list.Visible = false;
        _message.Visible = true;
    }

    private void ChooseSelected()
    {
        if (_list.SelectedItem is PlaylistSummary playlist)
        {
            PlaylistChosen?.Invoke(playlist);
        }
    }

    private void SetHover(int index)
    {
        if (index == _hoverIndex)
        {
            return;
        }

        var previous = _hoverIndex;
        _hoverIndex = index;
        InvalidateRow(previous);
        InvalidateRow(index);
    }

    private void InvalidateRow(int index)
    {
        if (index >= 0 && index < _list.Items.Count)
        {
            _list.Invalidate(_list.GetItemRectangle(index));
        }
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _list.Items[e.Index] is not PlaylistSummary playlist)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        var back = selected ? SelectedColor : e.Index == _hoverIndex ? HoverColor : Surface;
        using (var brush = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        var padding = this.Px(12);
        var half = e.Bounds.Height / 2;
        var top = new Rectangle(e.Bounds.X + padding, e.Bounds.Y + 3, e.Bounds.Width - 2 * padding, half);
        var bottom = new Rectangle(top.X, e.Bounds.Y + half - 1, top.Width, half - 2);
        const TextFormatFlags Flags =
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        TextRenderer.DrawText(e.Graphics, playlist.Name, _nameFont, top, TextColor, Flags | TextFormatFlags.Bottom);
        TextRenderer.DrawText(e.Graphics, playlist.Details, _detailFont, bottom, DimColor, Flags | TextFormatFlags.Top);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameFont.Dispose();
            _detailFont.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);
}
