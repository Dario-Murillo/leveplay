namespace Leveplay.Tray;

/// <summary>Small dialog that asks for the Soloist API key without echoing it.</summary>
internal sealed class ApiKeyPrompt : Form
{
    private readonly TextBox _input = new() { UseSystemPasswordChar = true, Dock = DockStyle.Top };

    private ApiKeyPrompt()
    {
        Text = "Leveplay — Soloist API key";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 130);
        Padding = new Padding(12);

        var label = new Label
        {
            Text = "Paste the API key from developer.spotify.com/dashboard/soloist.\n" +
                   "It is stored encrypted for your Windows user.",
            Dock = DockStyle.Top,
            Height = 44,
        };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 36,
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(_input);
        Controls.Add(label);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Returns the trimmed key, or null when the dialog was cancelled or left empty.</summary>
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
