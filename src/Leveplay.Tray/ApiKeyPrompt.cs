using System.Diagnostics;

namespace Leveplay.Tray;

/// <summary>Dialog that asks for the Soloist API key without echoing it.</summary>
internal sealed class ApiKeyPrompt : Form
{
    private const string DashboardUrl = "https://developer.spotify.com/dashboard/soloist";

    private readonly TextBox _input;
    private readonly Button _save;

    private ApiKeyPrompt()
    {
        Text = "Leveplay";
        Font = SystemFonts.MessageBoxFont ?? Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;
        ShowIcon = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(24, 20, 24, 20);

        var title = new Label
        {
            Text = "Soloist API key",
            Font = new Font(Font.FontFamily, Font.Size + 4, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        var description = new Label
        {
            Text = "Soloist plays the audio on this computer and needs an API key from your " +
                   "Spotify Premium account. The key is stored encrypted for your Windows user.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Margin = new Padding(0, 0, 0, 6),
        };
        var link = new LinkLabel
        {
            Text = "Create or copy a key in the Spotify dashboard",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 18),
        };
        link.LinkClicked += (_, _) =>
            Process.Start(new ProcessStartInfo(DashboardUrl) { UseShellExecute = true });

        var fieldLabel = new Label
        {
            Text = "API key",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        _input = new TextBox
        {
            UseSystemPasswordChar = true,
            Width = 456,
            Margin = new Padding(3, 0, 0, 8),
        };
        var show = new CheckBox
        {
            Text = "Show key",
            AutoSize = true,
            Margin = new Padding(4, 0, 0, 22),
        };
        show.CheckedChanged += (_, _) => _input.UseSystemPasswordChar = !show.Checked;

        _save = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Enabled = false,
            AutoSize = true,
            MinimumSize = new Size(96, 32),
            Margin = new Padding(8, 0, 0, 0),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            MinimumSize = new Size(96, 32),
            Margin = new Padding(8, 0, 0, 0),
        };
        _input.TextChanged += (_, _) => _save.Enabled = _input.Text.Trim().Length > 0;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_save);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        foreach (Control control in new Control[] { title, description, link, fieldLabel, _input, show, buttons })
        {
            layout.Controls.Add(control);
        }

        Controls.Add(layout);
        AcceptButton = _save;
        CancelButton = cancel;
        ActiveControl = _input;
    }

    /// <summary>Returns the trimmed key, or null when the dialog was cancelled.</summary>
    public static string? Ask()
    {
        using var prompt = new ApiKeyPrompt();
        if (prompt.ShowDialog() != DialogResult.OK)
        {
            return null;
        }

        var key = prompt._input.Text.Trim();
        return key.Length == 0 ? null : key;
    }
}
