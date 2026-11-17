namespace Leveplay.App.Player;

/// <summary>A window that only hides when the user closes it; the tray icon keeps the app alive.</summary>
internal abstract class HideOnCloseForm : Form
{
    private bool _allowClose;

    /// <summary>Lets the window really close when the app is exiting.</summary>
    public void CloseForExit()
    {
        _allowClose = true;
        Close();
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
}
