using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Leveplay.Tray.Player;

/// <summary>
/// Thin owner-drawn slider used for both the progress bar and the volume. The stock
/// TrackBar cannot be themed and looks out of place in a dark window.
/// </summary>
internal sealed class SliderBar : Control
{
    private double _value;
    private bool _dragging;
    private bool _hovering;

    public SliderBar()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TrackColor { get; set; } = Color.FromArgb(52, 52, 61);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = Color.FromArgb(124, 92, 255);

    /// <summary>True while the user holds the thumb; updates from outside are ignored then.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Position from 0 to 1.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set
        {
            var clamped = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
            if (_dragging || Math.Abs(clamped - _value) < 0.0005)
            {
                return;
            }

            _value = clamped;
            Invalidate();
        }
    }

    /// <summary>Raised continuously while dragging, for live feedback.</summary>
    public event Action<double>? ValueChanging;

    /// <summary>Raised once when the user releases the mouse.</summary>
    public event Action<double>? ValueCommitted;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float thickness = Math.Max(3f, Height / 5f);
        float thumb = Math.Min(Height - 2f, thickness * 3f);
        float left = thumb / 2f;
        float width = Math.Max(1f, Width - thumb);
        float top = (Height - thickness) / 2f;

        using var track = new SolidBrush(Enabled ? TrackColor : Color.FromArgb(40, 40, 46));
        using var fill = new SolidBrush(Enabled ? FillColor : Color.FromArgb(80, 80, 90));
        FillRounded(g, track, left, top, width, thickness);

        float filled = (float)(width * _value);
        if (filled > 0.5f)
        {
            FillRounded(g, fill, left, top, filled, thickness);
        }

        if (Enabled && (_hovering || _dragging))
        {
            using var knob = new SolidBrush(Color.White);
            g.FillEllipse(knob, left + filled - thumb / 2f, (Height - thumb) / 2f, thumb, thumb);
        }
    }

    private static void FillRounded(Graphics g, Brush brush, float x, float y, float width, float height)
    {
        if (width <= height)
        {
            g.FillEllipse(brush, x, y, Math.Max(width, 1f), height);
            return;
        }

        using var path = new GraphicsPath();
        path.AddArc(x, y, height, height, 90, 180);
        path.AddArc(x + width - height, y, height, height, 270, 180);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled)
        {
            _dragging = true;
            Capture = true;
            MoveTo(e.X);
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            MoveTo(e.X);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
            ValueCommitted?.Invoke(_value);
        }

        base.OnMouseUp(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovering = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovering = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    private void MoveTo(int x)
    {
        float thumb = Math.Min(Height - 2f, Math.Max(3f, Height / 5f) * 3f);
        double width = Math.Max(1f, Width - thumb);
        _value = Math.Clamp((x - thumb / 2f) / width, 0, 1);
        Invalidate();
        ValueChanging?.Invoke(_value);
    }
}
