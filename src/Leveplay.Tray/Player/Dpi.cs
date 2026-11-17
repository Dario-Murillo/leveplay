namespace Leveplay.Tray.Player;

/// <summary>
/// Layout coordinates in this project are written for a 150 % display (144 dpi), which
/// is where the windows were designed. Forms declare that with <see cref="DesignSize"/>
/// so WinForms scales them, fonts included, for any other setting.
/// </summary>
internal static class Dpi
{
    public const float Design = 144F;

    public static SizeF DesignSize => new(Design, Design);

    /// <summary>Converts a design-time length to pixels on the control's display.</summary>
    public static int Px(this Control control, int designPixels) =>
        (int)Math.Round(designPixels * control.DeviceDpi / Design);
}
