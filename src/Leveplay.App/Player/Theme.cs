using System.Runtime.InteropServices;

namespace Leveplay.App.Player;

/// <summary>Colours, icon glyphs and small helpers shared by the player windows.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(23, 23, 28);
    public static readonly Color Surface = Color.FromArgb(30, 30, 37);
    public static readonly Color Raised = Color.FromArgb(38, 38, 46);
    public static readonly Color Hover = Color.FromArgb(48, 48, 58);
    public static readonly Color Selected = Color.FromArgb(66, 52, 130);
    public static readonly Color Accent = Color.FromArgb(124, 92, 255);
    public static readonly Color Text = Color.FromArgb(242, 242, 245);
    public static readonly Color Dim = Color.FromArgb(154, 154, 165);
    public static readonly Color Warning = Color.FromArgb(255, 176, 96);

    // Segoe MDL2 Assets ships with Windows 10 and 11.
    public const string IconFont = "Segoe MDL2 Assets";
    public const string PreviousGlyph = "";
    public const string PlayGlyph = "";
    public const string PauseGlyph = "";
    public const string NextGlyph = "";
    public const string VolumeGlyph = "";
    public const string LibraryGlyph = "";
    public const string RefreshGlyph = "";

    public static Font BaseFont => SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

    /// <summary>Flat, borderless button showing a single icon glyph.</summary>
    public static Button IconButton(string glyph, string accessibleName, float size = 11F)
    {
        var button = new Button
        {
            Text = glyph,
            Font = new Font(IconFont, size),
            FlatStyle = FlatStyle.Flat,
            BackColor = Background,
            ForeColor = Text,
            AccessibleName = accessibleName,
            TabStop = false,
            UseMnemonic = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Hover;
        button.FlatAppearance.MouseDownBackColor = Raised;
        return button;
    }

    /// <summary>Gives a scrollable control the dark scrollbars Explorer uses.</summary>
    public static void UseDarkScrollbars(Control control) =>
        _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);

    /// <summary>Asks Windows to draw the title bar dark. Ignored on versions without support.</summary>
    public static void UseDarkTitleBar(Form form)
    {
        const int UseImmersiveDarkMode = 20;
        var enabled = 1;
        _ = DwmSetWindowAttribute(form.Handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
