using System.Drawing.Drawing2D;

namespace ForceBond.Launcher;

internal sealed class SessionProgress : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 40 };
    private SessionView _view = SessionView.Create(false, false, null);
    private SessionState? _state;
    private float _fade = 1;
    private readonly StyledButton _story = new() { Text = "Story", Location = new Point(18, 103), Size = new Size(74, 30) };
    private readonly StyledButton _storyPlus = new() { Text = "Story+", Location = new Point(100, 103), Size = new Size(74, 30) };
    private readonly StyledButton _futureMode = new() { Text = "Roleplay", Location = new Point(182, 103), Size = new Size(84, 30), Enabled = false, ForeColor = Color.Gray };
    private string _selectedMode = "story";

    public SessionProgress()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(9, 22, 24); AccessibleName = "Live co-op session";
        _selectedMode = LauncherSettings.Load().Mode;
        _story.Click += (_, _) => SaveMode("story");
        _storyPlus.Click += (_, _) => SaveMode("story+");
        Controls.Add(_story); Controls.Add(_storyPlus); Controls.Add(_futureMode);
        UpdateModeButtons();
        _animation.Tick += (_, _) => { _fade = Math.Min(1, _fade + .1f); Invalidate(); if (_fade >= 1) _animation.Stop(); };
    }
    public void UpdateState(SessionView view, SessionState? state = null)
    {
        if (_view.RuntimeText != view.RuntimeText) { _fade = .2f; _animation.Start(); }
        _view = view; _state = state;
        _selectedMode = state?.Text("RuntimeEngine") == "OdysseyMP" ? LauncherSettings.NormalizeMode(state.Text("Mode")) : LauncherSettings.Load().Mode;
        _story.Enabled = _storyPlus.Enabled = !view.Steps[0];
        UpdateModeButtons();
        AccessibleDescription = L.T(view.Description); Invalidate();
    }
    private void SaveMode(string mode)
    {
        try {
            (LauncherSettings.Load() with { Mode = mode }).Save();
            _selectedMode = mode; UpdateModeButtons(); Invalidate();
        } catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Could not save settings")); }
    }
    private void UpdateModeButtons()
    {
        foreach (var (button, mode) in new[] { (_story, "story"), (_storyPlus, "story+") }) {
            bool selected = _selectedMode == mode;
            button.FillColor = selected ? Color.FromArgb(22, 63, 58) : Color.FromArgb(12, 30, 31);
            button.BorderColor = selected ? Color.FromArgb(48, 210, 181) : Color.FromArgb(24, 113, 102);
            button.ForeColor = Color.FromArgb(48, 210, 181);
            button.AccessibleDescription = selected ? L.T("Selected mode") : L.T("Session mode");
        }
    }
    internal static string LocationName(string module) => module.ToUpperInvariant() switch {
        "101PER" => "Peragus · Administration Level", "102PER" => "Peragus · Mining Tunnels",
        "103PER" => "Peragus · Fuel Depot", "104PER" => "Peragus · Asteroid Exterior",
        "105PER" => "Harbinger", "106PER" => "Peragus · Hangar",
        _ when module.EndsWith("TEL", StringComparison.OrdinalIgnoreCase) => "Telos",
        _ when module.EndsWith("NAR", StringComparison.OrdinalIgnoreCase) => "Nar Shaddaa",
        _ when module.EndsWith("DAN", StringComparison.OrdinalIgnoreCase) => "Dantooine",
        _ when module.EndsWith("KOR", StringComparison.OrdinalIgnoreCase) => "Korriban",
        _ when module.EndsWith("DXN", StringComparison.OrdinalIgnoreCase) => "Dxun",
        _ when module.EndsWith("OND", StringComparison.OrdinalIgnoreCase) => "Onderon",
        _ when module.EndsWith("EBO", StringComparison.OrdinalIgnoreCase) => "Ebon Hawk",
        "" => "No location loaded", _ => module };
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Color teal = Color.FromArgb(48, 210, 181), gold = Color.FromArgb(202, 166, 96), muted = Color.FromArgb(143, 166, 160), white = Color.FromArgb(228, 229, 215);
        using var border = new Pen(Color.FromArgb(28, 72, 67)); g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var accent = new Pen(gold, 2); g.DrawLine(accent, 0, 1, 72, 1);
        string title = !_view.Steps[0] ? "READY FOR YOUR NEXT JOURNEY" : !_view.Steps[1] ? "CONNECTING FORCE BOND" : !_view.Steps[2] ? "PREPARING YOUR WORLD" : !_view.Steps[3] ? "WAITING FOR PLAYERS" : !_view.Steps[4] ? "PLAYERS ARE CONNECTING" : "THE FORCE BOND IS ACTIVE";
        if (_state?.Text("RuntimeEngine") == "OdysseyMP") title = _state.Flag("Prologue") ? "PROLOGUE / CO-OP WAITING" : !_state.Flag("WorldLoaded") ? "READY FOR YOUR NEXT JOURNEY" : !_state.Flag("SessionReady") ? "CREATING STEAM SESSION" : !_state.Flag("PlayersInWorld") ? "WAITING FOR PLAYERS" : "THE FORCE BOND IS ACTIVE";
        using var heading = new Font("Segoe UI Semibold", 13, FontStyle.Bold);
        TextRenderer.DrawText(g, L.T(title), heading, new Rectangle(18, 18, Width - 36, 30), _view.Warning ? gold : Color.FromArgb((int)(150 + 78 * _fade), 229, 215), TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, L.T(LocationName(_state?.Text("Module") ?? "")), Font, new Rectangle(18, 53, Width - 36, 24), muted, TextFormatFlags.EndEllipsis);
        g.DrawLine(border, 18, 89, Width - 18, 89);
        TextRenderer.DrawText(g, L.T(_selectedMode == "story+" ? "Up to 8 players per session" : "Up to 3 players per session"), Font, new Rectangle(276, 108, Width - 294, 22), gold, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, L.T(_selectedMode == "story+" ? "Own characters · Personal inventory" : "Host's companions · Shared inventory"), Font, new Rectangle(18, 140, Width - 36, 24), white, TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing) { if (disposing) { _animation.Dispose(); } base.Dispose(disposing); }
}
