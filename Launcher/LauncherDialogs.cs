namespace ForceBond.Launcher;

internal class UtilityWindow : Form
{
    protected static readonly Color Ink = Color.FromArgb(228, 229, 215);
    protected static readonly Color Teal = Color.FromArgb(48, 210, 181);
    protected static readonly Color Gold = Color.FromArgb(202, 166, 96);
    protected UtilityWindow(string title, Size size)
    {
        Text = title; ClientSize = size; FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(7, 16, 18);
        ForeColor = Ink; Font = new Font("Segoe UI", 10); ShowInTaskbar = false;
        Controls.Add(new Label { Text = title.ToUpperInvariant(), ForeColor = Gold, Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) });
        var close = MakeButton("×", new Point(size.Width - 52, 12), new Size(36, 32));
        close.AccessibleName = "Close " + title; close.DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => Close(); Controls.Add(close); CancelButton = close;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(24, 113, 102));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        e.Graphics.DrawLine(pen, 24, 58, Width - 24, 58);
    }
    protected static StyledButton MakeButton(string text, Point at, Size size) => new StyledButton
    {
        Text = text, Location = at, Size = size, Cursor = Cursors.Hand,
        FillColor = Color.FromArgb(12, 30, 31), ForeColor = Teal, BorderColor = Color.FromArgb(24, 113, 102),
        Font = new Font("Segoe UI", 9, FontStyle.Bold), Animate = false
    };
    protected void Caption(string text, int y) => Controls.Add(new Label { Text = text, Location = new Point(26, y), AutoSize = true, ForeColor = Ink });
}

internal sealed class SettingsWindow : UtilityWindow
{
    private sealed record KeyChoice(int Code, string Name);
    private sealed record LanguageChoice(string Code, string Name);
    public SettingsWindow(LauncherSettings current, Func<Task<string>>? verifyFiles = null) : base("Session settings", new Size(540, 734))
    {
        Caption("Language", 91); Caption("Text chat", 143); Caption("Proximity microphone", 195); Caption("Partner voice volume", 254);
        var language = ChoiceBox(new[] { new LanguageChoice("en", "English"), new LanguageChoice("uk", "Українська"), new LanguageChoice("ru", "Русский") }, current.Language, 86);
        language.AccessibleName = "Language";
        ComboBox chat = KeyBox(LauncherSettings.ChatKeys, current.ChatKey, 138);
        ComboBox voice = KeyBox(LauncherSettings.VoiceKeys, current.VoiceKey, 190);
        var volume = new TrackBar { Minimum = 0, Maximum = 100, Value = current.VoiceVolume, TickFrequency = 25, Location = new Point(270, 243), Size = new Size(205, 45), BackColor = BackColor, AccessibleName = "Partner voice volume" };
        var percent = new Label { Text = $"{volume.Value}%", Location = new Point(477, 254), Size = new Size(50, 24), ForeColor = Teal };
        volume.ValueChanged += (_, _) => percent.Text = $"{volume.Value}%";
        Controls.Add(volume); Controls.Add(percent);
        Caption("Overhead chat distance", 308);
        var bubbles = new TrackBar { Minimum = 0, Maximum = 100, Value = current.ChatBubbleRange, TickFrequency = 25, Location = new Point(270, 297), Size = new Size(205, 45), BackColor = BackColor, AccessibleName = "Overhead chat distance" };
        var distance = new Label { Text = $"{bubbles.Value} m", Location = new Point(477, 308), Size = new Size(50, 24), ForeColor = Teal };
        bubbles.ValueChanged += (_, _) => distance.Text = bubbles.Value == 0 ? L.T("Off") : $"{bubbles.Value} m";
        Controls.Add(bubbles); Controls.Add(distance);
        var discord = new CheckBox { Text = "Show Discord Rich Presence", Checked = current.DiscordEnabled, Location = new Point(26, 368), Size = new Size(380, 30), ForeColor = Ink, AutoSize = false };
        Controls.Add(discord);
        var debug = new CheckBox { Text = "Enable Debug Menu (F7, host only)", Checked = current.DebugMenuEnabled, Location = new Point(26, 402), Size = new Size(488, 30), ForeColor = Ink };
        debug.CheckedChanged += (_, _) => { if (debug.Checked && (int)voice.SelectedValue! == 118) voice.SelectedValue = 86; };
        Controls.Add(debug);
        var borderless = new CheckBox { Text = "Borderless Fullscreen (restart required)", Checked = current.BorderlessFullscreen, Location = new Point(26, 438), Size = new Size(488, 30), ForeColor = Ink };
        Controls.Add(borderless);
        Controls.Add(new Label { Text = "Chat, voice and Debug settings apply while the game is running.\nHold the microphone key to talk; double-tap to lock. F8 invites through Steam.", Location = new Point(26, 478), Size = new Size(490, 48), ForeColor = Color.FromArgb(143, 166, 160), Font = new Font("Segoe UI", 8.5f) });
        Caption("Game language", 534);
        var gameLanguage = ChoiceBox(new[] { new LanguageChoice("en", "English"), new LanguageChoice("ru", "Русский (6,63%)") }, current.GameLanguage, 529);
        gameLanguage.AccessibleName = "Game language";
        var save = MakeButton("SAVE SETTINGS", new Point(334, 681), new Size(180, 36));
        save.Click += (_, _) =>
        {
            try
            {
                new LauncherSettings((int)chat.SelectedValue!, (int)voice.SelectedValue!, volume.Value, discord.Checked, (string)language.SelectedValue!, bubbles.Value, debug.Checked, borderless.Checked, current.Mode, (string)gameLanguage.SelectedValue!).Save();
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { MessageBox.Show(this, L.T(ex.Message), L.T("Could not save settings"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        var verify = MakeButton("VERIFY GAME FILES", new Point(26, 578), new Size(488, 36));
        var verificationStatus = new Label { Text = "Checks and repairs Force Bond files. Close KOTOR II first.", Location = new Point(26, 622), Size = new Size(488, 42), ForeColor = Color.FromArgb(143, 166, 160), Font = new Font("Segoe UI", 8.5f) };
        verify.Enabled = verifyFiles is not null;
        verify.Click += async (_, _) =>
        {
            if (verifyFiles is null) return;
            verify.Enabled = false; save.Enabled = false;
            foreach (Control control in Controls) if (control is IButtonControl) control.Enabled = false;
            ControlBox = false;
            verificationStatus.Text = L.T("Checking Force Bond files...");
            UseWaitCursor = true;
            bool busy = true;
            FormClosingEventHandler preventClose = (_, e) => { if (busy) e.Cancel = true; };
            FormClosing += preventClose;
            try
            {
                string report = await verifyFiles();
                verificationStatus.Text = L.T("File verification completed.");
                MessageBox.Show(this, report, L.T("File verification"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                verificationStatus.Text = L.T("File verification failed.");
                MessageBox.Show(this, L.T(ex.Message), L.T("File verification"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                busy = false; FormClosing -= preventClose;
                UseWaitCursor = false;
                foreach (Control control in Controls) if (control is IButtonControl) control.Enabled = true;
            }
        };
        Controls.Add(verify); Controls.Add(verificationStatus);
        Controls.Add(save); AcceptButton = save;
        L.Apply(this);
    }
    private ComboBox KeyBox(int[] keys, int selected, int y) => ChoiceBox(keys.Select(key => new KeyChoice(key, LauncherSettings.KeyName(key))).ToArray(), selected, y);
    private ComboBox ChoiceBox(Array choices, object selected, int y)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(280, y), Size = new Size(230, 30), BackColor = Color.FromArgb(16, 36, 37), ForeColor = Ink, FlatStyle = FlatStyle.Flat,
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22,
            DisplayMember = nameof(KeyChoice.Name), ValueMember = nameof(KeyChoice.Code), DataSource = choices };
        box.DrawItem += (_, e) =>
        {
            using var fill = new SolidBrush(box.BackColor); e.Graphics.FillRectangle(fill, e.Bounds);
            string name = e.Index >= 0 ? box.GetItemText(box.Items[e.Index]) ?? "" : box.Text;
            TextRenderer.DrawText(e.Graphics, name, box.Font, e.Bounds, Teal, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        };
        Controls.Add(box); box.SelectedValue = selected; return box;
    }
}

internal sealed class DiagnosticsWindow : UtilityWindow
{
    public DiagnosticsWindow(string report, Action openLog) : base("Session diagnostics", new Size(800, 570))
    {
        var text = new TextBox { Text = report, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Location = new Point(24, 80), Size = new Size(752, 410), BackColor = Color.FromArgb(12, 25, 27), ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9), AccessibleName = "Session diagnostic report" };
        Controls.Add(text);
        var copy = MakeButton("COPY REPORT", new Point(596, 512), new Size(180, 36));
        copy.Click += (_, _) => { try { Clipboard.SetText(report); copy.Text = "COPIED"; } catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Clipboard unavailable")); } };
        var log = MakeButton("OPEN GAME LOG", new Point(394, 512), new Size(188, 36));
        log.Click += (_, _) => openLog(); Controls.Add(copy); Controls.Add(log);
        L.Apply(this);
    }
}
