using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace ForceBond.Launcher;

// A user-painted control, not a native BUTTON with a second competing renderer.
internal sealed class StyledButton : Control, IButtonControl
{
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 60 };
    private bool _hovered, _pressed, _default;
    private double _phase;
    private Color _fill = Color.FromArgb(12, 30, 31), _border = Color.FromArgb(24, 113, 102);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Animate { get => _pulse.Enabled; set { if (value) _pulse.Start(); else _pulse.Stop(); Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get => _fill; set { _fill = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get => _border; set { _border = value; Invalidate(); } }
    [DefaultValue(DialogResult.None)] public DialogResult DialogResult { get; set; }

    public StyledButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        BackColor = Color.Transparent;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        _pulse.Tick += (_, _) => { _phase = (_phase + .08) % (Math.PI * 2); if (Visible && Enabled) Invalidate(); };
    }

    public void NotifyDefault(bool value) { _default = value; Invalidate(); }
    public void PerformClick()
    {
        if (!Enabled || !Visible) return;
        if (DialogResult != DialogResult.None && FindForm() is Form form) form.DialogResult = DialogResult;
        OnClick(EventArgs.Empty);
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Space || base.IsInputKey(keyData);
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Enter && Enabled) { PerformClick(); return true; }
        return base.ProcessDialogKey(keyData);
    }
    protected override bool ProcessMnemonic(char charCode)
    {
        if (CanSelect && IsMnemonic(charCode, Text)) { Focus(); PerformClick(); return true; }
        return base.ProcessMnemonic(charCode);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && e.Modifiers == Keys.None) { _pressed = true; e.Handled = e.SuppressKeyPress = true; Invalidate(); }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { bool fire = _pressed; _pressed = false; e.Handled = true; Invalidate(); if (fire) PerformClick(); }
        base.OnKeyUp(e);
    }
    protected override void OnMouseEnter(EventArgs e) { _hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Focus(); _pressed = true; Capture = true; Invalidate(); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool fire = e.Button == MouseButtons.Left && _pressed && ClientRectangle.Contains(e.Location);
        _pressed = false; Capture = false; Invalidate(); base.OnMouseUp(e);
        if (fire) PerformClick();
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) { _pressed = false; Invalidate(); } base.OnMouseCaptureChanged(e); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; base.OnLostFocus(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) _pressed = _hovered = false; base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float radius = 6f * DeviceDpi / 96f;
        using var shape = Shape(new RectangleF(1, 1, Width - 2, Height - 2), radius);
        int lift = !Enabled ? 0 : _pressed ? 3 : _hovered ? 14 : Animate ? (int)(3 + Math.Sin(_phase) * 3) : 0;
        Color fill = !Enabled ? Color.FromArgb(31, 48, 47) : Color.FromArgb(Math.Min(255, _fill.R + lift), Math.Min(255, _fill.G + lift), Math.Min(255, _fill.B + lift));
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(!Enabled ? Color.FromArgb(42, 68, 63) : _hovered || _default ? ControlPaint.Light(_border, .25f) : _border, 1);
        g.FillPath(brush, shape);
        g.DrawPath(pen, shape);
        // No moving gradient/shine: repeated paints cover exactly the same area.
        if (Focused && ShowFocusCues)
        {
            using var focus = Shape(new RectangleF(4, 4, Width - 8, Height - 8), Math.Max(2, radius - 2));
            using var focusPen = new Pen(Color.FromArgb(100, ForeColor)) { DashStyle = DashStyle.Dot };
            g.DrawPath(focusPen, focus);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    private static GraphicsPath Shape(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ActionAccessibility(this);
    private sealed class ActionAccessibility(StyledButton owner) : ControlAccessibleObject(owner)
    {
        public override string DefaultAction => "Press";
        public override void DoDefaultAction() => owner.PerformClick();
    }
    protected override void Dispose(bool disposing) { if (disposing) _pulse.Dispose(); base.Dispose(disposing); }
}
