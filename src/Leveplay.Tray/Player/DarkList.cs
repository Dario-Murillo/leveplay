namespace Leveplay.Tray.Player;

/// <summary>What a <see cref="DarkList"/> row shows.</summary>
/// <param name="Trailing">Short right-aligned text, such as a duration.</param>
internal readonly record struct RowText(string Primary, string? Secondary = null, string? Trailing = null);

/// <summary>
/// Owner-drawn list with two-line rows, hover and a dark scrollbar. The stock ListBox
/// cannot be themed. Items are plain objects; <see cref="Describe"/> turns them into text.
/// </summary>
internal sealed class DarkList : ListBox
{
    private const int RowHeight = 46;

    private readonly Font _primaryFont;
    private readonly Font _secondaryFont;
    private int _hoverIndex = -1;

    public DarkList()
    {
        var baseFont = Theme.BaseFont;
        _primaryFont = new Font(baseFont.FontFamily, baseFont.Size + 0.5F);
        _secondaryFont = new Font(baseFont.FontFamily, baseFont.Size - 1F);

        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = RowHeight;
        IntegralHeight = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    /// <summary>Maps an item to the text of its row.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<object, RowText> Describe { get; init; } = item => new RowText(item.ToString() ?? "");

    /// <summary>Raised when an item is double-clicked or chosen with Enter.</summary>
    public event Action<object>? ItemActivated;

    public void SetItems(IEnumerable<object> items)
    {
        BeginUpdate();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        EndUpdate();
        _hoverIndex = -1;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Owner-drawn rows are not scaled automatically.
        ItemHeight = this.Px(RowHeight);
        Theme.UseDarkScrollbars(this);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            return;
        }

        var row = Describe(Items[e.Index]);
        var selected = (e.State & DrawItemState.Selected) != 0;
        var back = selected ? Theme.Selected : e.Index == _hoverIndex ? Theme.Hover : Theme.Surface;
        using (var brush = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        const TextFormatFlags Flags =
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var padding = this.Px(12);
        var textArea = Rectangle.FromLTRB(
            e.Bounds.Left + padding, e.Bounds.Top, e.Bounds.Right - padding, e.Bounds.Bottom);

        if (!string.IsNullOrEmpty(row.Trailing))
        {
            var width = TextRenderer.MeasureText(e.Graphics, row.Trailing, _secondaryFont).Width + padding / 2;
            var trailing = Rectangle.FromLTRB(textArea.Right - width, textArea.Top, textArea.Right, textArea.Bottom);
            TextRenderer.DrawText(e.Graphics, row.Trailing, _secondaryFont, trailing, Theme.Dim,
                Flags | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            textArea.Width -= width + padding / 2;
        }

        if (string.IsNullOrEmpty(row.Secondary))
        {
            TextRenderer.DrawText(e.Graphics, row.Primary, _primaryFont, textArea, Theme.Text,
                Flags | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        var half = textArea.Height / 2;
        var top = new Rectangle(textArea.X, textArea.Y + 3, textArea.Width, half);
        var bottom = new Rectangle(textArea.X, textArea.Y + half - 1, textArea.Width, half - 2);
        TextRenderer.DrawText(e.Graphics, row.Primary, _primaryFont, top, Theme.Text,
            Flags | TextFormatFlags.Left | TextFormatFlags.Bottom);
        TextRenderer.DrawText(e.Graphics, row.Secondary, _secondaryFont, bottom, Theme.Dim,
            Flags | TextFormatFlags.Left | TextFormatFlags.Top);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        SetHover(IndexFromPoint(e.Location));
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        SetHover(-1);
        base.OnMouseLeave(e);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        Activate();
        base.OnDoubleClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            Activate();
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void Activate()
    {
        if (SelectedItem is { } item)
        {
            ItemActivated?.Invoke(item);
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
        if (index >= 0 && index < Items.Count)
        {
            Invalidate(GetItemRectangle(index));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _primaryFont.Dispose();
            _secondaryFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
