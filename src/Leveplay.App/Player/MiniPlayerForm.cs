namespace Leveplay.App.Player;

/// <summary>
/// Small fixed-size window that shows only the now-playing strip, next to the tray.
/// Closing it only hides it; the tray icon keeps the app alive.
/// </summary>
internal sealed class MiniPlayerForm : HideOnCloseForm
{
    private const int DesignWidth = 440;

    /// <summary>The strip this window hosts; the app wires its events and feeds it state.</summary>
    public PlayerPanel Player { get; }

    public MiniPlayerForm(Icon icon)
    {
        Text = "Leveplay";
        Icon = icon;
        BackColor = Theme.Background;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;

        Player = new PlayerPanel(Theme.LibraryGlyph, "Open Leveplay") { Dock = DockStyle.Fill };
        Controls.Add(Player);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ClientSize = new Size(this.Px(DesignWidth), this.Px(PlayerPanel.DesignHeight));
        Theme.UseDarkTitleBar(this);
    }

    /// <summary>Shows the window in the corner next to the tray, or hides it if visible.</summary>
    public void Toggle()
    {
        if (Visible)
        {
            Hide();
            return;
        }

        _ = Handle; // Make sure the size has been applied before positioning.
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        if (!Player.HandleShortcut(e))
        {
            base.OnKeyDown(e);
        }
    }
}
