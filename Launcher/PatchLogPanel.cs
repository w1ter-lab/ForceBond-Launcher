using System.Globalization;
using System.Text.Json;

namespace ForceBond.Launcher;

internal sealed record PatchNote(string Kind, string En, string Uk, string Ru)
{
    public string Localized => L.Language switch { "uk" => Uk, "ru" => Ru, _ => En };
}
internal sealed record PatchRelease(string Version, string Date, PatchNote[] Entries);

internal sealed class PatchLogPanel : UserControl
{
    // Scrolling is an offset inside a clipped viewport. Neither control owns
    // native scrollbars, so ShowScrollBar cannot trigger a layout/measure loop.
    private readonly Panel _viewport = new() { AutoScroll = false };
    private readonly FlowLayoutPanel _flow = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(18, 16, 18, 18) };
    private readonly Font _heading = new("Segoe UI Semibold", 12, FontStyle.Bold);
    private readonly Font _version = new("Segoe UI Semibold", 10, FontStyle.Bold);
    private readonly Font _caption = new("Segoe UI", 8, FontStyle.Bold);
    private readonly Font _body = new("Segoe UI", 9);
    private readonly ScrollRail _rail;
    private readonly HashSet<string> _expanded = [];
    private bool _historyVisible, _reflowing, _moving;
    private int _offset;
    private static readonly Color Teal = Color.FromArgb(48, 210, 181), Gold = Color.FromArgb(202, 166, 96), Muted = Color.FromArgb(143, 166, 160);
    private int ViewHeight => Math.Max(1, _viewport.ClientSize.Height);
    private int ContentHeight => Math.Max(1, _flow.Height);
    private int ScrollLimit => Math.Max(0, ContentHeight - ViewHeight);

    public PatchLogPanel()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(9, 22, 24); AccessibleName = "Patch Log";
        _viewport.BackColor = _flow.BackColor = BackColor;
        _viewport.Bounds = new Rectangle(1, 3, Math.Max(1, Width - 20), Math.Max(1, Height - 4));
        _viewport.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _viewport.Controls.Add(_flow); Controls.Add(_viewport);
        _rail = new ScrollRail(this) { Location = new Point(Width - 19, 3), Size = new Size(18, Math.Max(1, Height - 4)),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right };
        Controls.Add(_rail); _rail.BringToFront();
        _flow.Layout += (_, _) => UpdateScroll();
        _flow.MouseWheel += Wheel; _viewport.MouseWheel += Wheel; MouseWheel += Wheel;
        Reload();
    }
    private void Wheel(object? sender, MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs handled) { if (handled.Handled) return; handled.Handled = true; }
        MoveTo(_offset - e.Delta / 120 * 54);
    }
    private void MoveTo(int value) { _offset = Math.Clamp(value, 0, ScrollLimit); UpdateScroll(); }
    private void UpdateScroll()
    {
        if (_moving || _reflowing || IsDisposed) return;
        _moving = true;
        try {
            _offset = Math.Clamp(_offset, 0, ScrollLimit);
            if (_flow.Top != -_offset) _flow.Location = new Point(0, -_offset);
            _rail?.Invalidate();
        } finally { _moving = false; }
    }
    private void Reflow()
    {
        if (_reflowing || _flow is null || _viewport is null || IsDisposed) return;
        _reflowing = true; _flow.SuspendLayout();
        try {
            int width = Math.Max(1, _viewport.ClientSize.Width);
            _flow.MinimumSize = new Size(width, 0); _flow.MaximumSize = new Size(width, 0); _flow.Width = width;
            foreach (Control child in _flow.Controls) {
                child.Width = TextWidth;
                if (child is Label label) label.Height = MeasureHeight(label.Text, label.Font);
            }
        } finally { _flow.ResumeLayout(true); _reflowing = false; }
        UpdateScroll();
    }
    private int MeasureHeight(string text, Font font) => Math.Max(font.Height,
        TextRenderer.MeasureText(text, font, new Size(TextWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height + 2);

    public void Reload()
    {
        _flow.SuspendLayout();
        try {
            foreach (Control child in _flow.Controls.Cast<Control>().ToArray()) child.Dispose();
            AddText(L.T("PATCH LOG"), _heading, Gold, 0, 12);
            using Stream? stream = typeof(PatchLogPanel).Assembly.GetManifestResourceStream("ForceBond.Launcher.PatchNotes.json");
            var releases = stream is null ? [] : JsonSerializer.Deserialize<PatchRelease[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            bool current = true;
            foreach (var release in releases) {
                if (!current && !_historyVisible) break;
                if (current) AddText(L.T("CURRENT RELEASE"), _caption, Teal, 0, 4);
                bool expanded = current || _expanded.Contains(release.Version);
                string culture = L.Language switch { "uk" => "uk-UA", "ru" => "ru-RU", _ => "en-GB" };
                string stamp = DateTime.TryParseExact(release.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo(culture)) : release.Date;
                if (current) AddText("v" + release.Version + "  /  " + stamp, _version, Color.FromArgb(228, 229, 215), 0, 8);
                else AddToggle((expanded ? "− " : "+ ") + "v" + release.Version + "  /  " + stamp, () => {
                    if (!_expanded.Add(release.Version)) _expanded.Remove(release.Version);
                    Reload();
                });
                if (!expanded) { current = false; continue; }
                foreach (var group in release.Entries.GroupBy(note => note.Kind)) {
                    string category = group.Key switch { "testing" => "TESTING", "fixed" => "FIXED", "improved" or "changed" => "IMPROVED", _ => "ADDED" };
                    AddText(L.T(category), _caption, group.Key == "fixed" ? Gold : Teal, 5, 3);
                    foreach (var note in group) AddText("• " + note.Localized, _body, Color.FromArgb(185, 204, 196), 0, 7);
                }
                _flow.Controls.Add(new Panel { Height = 1, Width = TextWidth, BackColor = Color.FromArgb(28, 72, 67), Margin = new Padding(0, 9, 0, 0) });
                current = false;
            }
            if (releases.Length > 1) {
                string label = L.Language switch { "uk" => "Попередні збірки", "ru" => "Предыдущие сборки", _ => "Previous builds" };
                string hide = L.Language switch { "uk" => "Згорнути історію", "ru" => "Свернуть историю", _ => "Hide history" };
                AddToggle(_historyVisible ? hide : $"{label} ({releases.Length - 1})", () => { _historyVisible = !_historyVisible; Reload(); });
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException) {
            AddText(L.T("Release notes unavailable."), _body, Muted, 0, 0);
        }
        finally { _flow.ResumeLayout(true); Reflow(); }
    }

    private int TextWidth => Math.Max(80, _viewport.ClientSize.Width - _flow.Padding.Horizontal);
    private void AddToggle(string text, Action action)
    {
        var button = new Button { Text = text, Width = TextWidth, Height = 38,
            FlatStyle = FlatStyle.Flat, Font = _body, ForeColor = Teal, BackColor = BackColor,
            TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand, Margin = new Padding(0, 6, 0, 3) };
        button.FlatAppearance.BorderColor = Color.FromArgb(28, 72, 67);
        button.Click += (_, _) => action();
        _flow.Controls.Add(button);
    }
    private void AddText(string text, Font font, Color color, int top, int bottom) => _flow.Controls.Add(new Label {
        Text = text, AutoSize = false, Size = new Size(TextWidth, MeasureHeight(text, font)), Font = font, ForeColor = color,
        BackColor = Color.Transparent, Margin = new Padding(0, top, 0, bottom), UseMnemonic = false
    });
    protected override void OnResize(EventArgs e) { base.OnResize(e); Reflow(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(28, 72, 67));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var accent = new Pen(Gold, 2); e.Graphics.DrawLine(accent, 0, 1, 72, 1);
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _heading.Dispose(); _version.Dispose(); _caption.Dispose(); _body.Dispose(); }
    }

    private sealed class ScrollRail : Control
    {
        private readonly PatchLogPanel _owner;
        private int _grab;
        public ScrollRail(PatchLogPanel owner)
        {
            _owner = owner; DoubleBuffered = true; BackColor = Color.FromArgb(9, 22, 24);
            Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ScrollBar;
        }
        private int ThumbHeight => Math.Clamp((int)((long)Math.Max(1, Height) * _owner.ViewHeight / _owner.ContentHeight), Math.Min(30, Math.Max(1, Height)), Math.Max(1, Height));
        private int ThumbTop => _owner.ScrollLimit == 0 ? 0 : (int)((long)Math.Max(0, Height - ThumbHeight) * _owner._offset / _owner.ScrollLimit);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (_owner.ScrollLimit == 0 || Height <= 0) return;
            using var track = new Pen(Color.FromArgb(28, 72, 67)); e.Graphics.DrawLine(track, Width / 2, 8, Width / 2, Height - 8);
            using var thumb = new SolidBrush(Capture ? Color.FromArgb(112, 235, 208) : Teal);
            e.Graphics.FillRectangle(thumb, Width / 2 - 2, ThumbTop, 4, ThumbHeight);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            _grab = e.Y >= ThumbTop && e.Y < ThumbTop + ThumbHeight ? e.Y - ThumbTop : ThumbHeight / 2;
            Capture = true; Drag(e.Y);
        }
        private void Drag(int y) => _owner.MoveTo((int)((long)Math.Max(0, y - _grab) * _owner.ScrollLimit / Math.Max(1, Height - ThumbHeight)));
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) Drag(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) => _owner.Wheel(this, e);
    }

}

internal sealed class UtilityPatchLog : UtilityWindow
{
    public UtilityPatchLog() : base(L.T("PATCH LOG"), new Size(392, 680))
    {
        Controls.Add(new PatchLogPanel { Location = new Point(24, 78), Size = new Size(344, 578) });
        L.Apply(this);
    }
}
