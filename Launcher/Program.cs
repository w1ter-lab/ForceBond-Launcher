using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Text;
using Microsoft.Win32;
using ForceBond.Updates;

namespace ForceBond.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        LauncherWindowIcon.SetApplicationIdentity();
        if (LauncherUpdates.Redirect(args)) return;
        if (!args.Any(a => a.StartsWith("--preview", StringComparison.Ordinal)) && Environment.ProcessPath is string executable)
            try { DiscordLaunch.Register(executable); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { LauncherUpdates.Log("Discord launch registration failed: " + ex.Message); }
        CacheMaintenance.Run();
        ApplicationConfiguration.Initialize();
        L.Language = LauncherSettings.Load().Language;
        string? previewLanguage = args.FirstOrDefault(arg => arg.StartsWith("--language=", StringComparison.Ordinal));
        if (previewLanguage is not null && args.Any(arg => arg.StartsWith("--preview", StringComparison.Ordinal)))
        {
            L.Language = LauncherSettings.NormalizeLanguage(previewLanguage[11..]);
            args = args.Where(arg => arg != previewLanguage).ToArray();
        }
        if (args.Length == 1 && args[0] == "--preview-live")
        {
            using var preview = new LauncherForm { Text = "Force Bond — Visual QA", StartPosition = FormStartPosition.CenterScreen };
            Application.Run(preview);
            return;
        }
        if (args.Length == 2 && args[0].StartsWith("--preview", StringComparison.Ordinal))
        {
            using Form form = args[0] switch
            {
                "--preview-access" => new BetaAccessWindow(),
                "--preview-settings" => new SettingsWindow(new LauncherSettings(Language: L.Language)),
                "--preview-diagnostics" => new DiagnosticsWindow("FORCE BOND / DIAGNOSTICS\r\nNo game process is running.\r\n", () => { }),
                "--preview-update" => new UpdateWindow(new ReleaseManifest(1, "0.3.25", "Launcher and mod update\r\n\r\nImprovements and fixes\r\nComplete package verification\r\nPrevious build retained", "https://example.invalid/package.zip", 1, "", []), new SignedRelease("", "")),
                _ => new LauncherForm()
            };
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-3000, -3000); form.ShowInTaskbar = false;
            form.Show(); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(args[1]); form.Close();
            return;
        }
        Application.Run(new LauncherForm(DiscordLaunch.Requested(args)));
    }
}

internal sealed class LauncherForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(7, 13, 15);
    private static readonly Color PanelColor = Color.FromArgb(12, 25, 27);
    private static readonly Color RaisedColor = Color.FromArgb(16, 36, 37);
    private static readonly Color Teal = Color.FromArgb(48, 210, 181);
    private static readonly Color TealDim = Color.FromArgb(24, 113, 102);
    private static readonly Color Gold = Color.FromArgb(202, 166, 96);
    private static readonly Color MainText = Color.FromArgb(228, 229, 215);
    private static readonly Color MutedText = Color.FromArgb(143, 166, 160);

    private readonly StyledButton _accessButton = new();
    private readonly Label _status = new();
    private readonly Label _pathLabel = new();
    private readonly Label _steamStatus = new();
    private readonly Label _gameStatus = new();
    private readonly Label _runtimeStatus = new();
    private readonly StyledButton _playButton;
    private readonly SessionProgress _sessionProgress = new();
    private readonly PatchLogPanel _patchLog = new();
    private readonly bool _showPatchSidebar;
    private SessionState? _session;
    private SessionView _sessionView = SessionView.Create(false, false, null);
    private int? _gamePid;
    private string? _lastLogPath;
    private Image? _backdrop;
    private LauncherWindowIcon? _windowIcon;
    private static string BuildVersion => typeof(LauncherForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "development";
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 3000 };
    private readonly CancellationTokenSource _shutdown = new();
    private string? _gamePath;
    private string? _steamExe;
    private bool _working;
    private bool _startupComplete, _repairingResources, _verificationRequired;
    private string? _verificationAttemptPath, _verificationError;
    private static string WindowPositionFile => Path.Combine(LauncherSettings.DataDirectory, "launcher-window.json");

    public LauncherForm(bool discordLaunch = false)
    {
        Text = "KOTOR II: Force Bond";
        FormBorderStyle = FormBorderStyle.None;
        Point? savedPosition = LauncherWindowPlacement.Load(WindowPositionFile);
        Rectangle[] areas = Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        StartPosition = FormStartPosition.Manual;
        _showPatchSidebar = LauncherWindowPlacement.ChooseArea(savedPosition, areas, Cursor.Position).Width >= 1380;
        ClientSize = new Size(_showPatchSidebar ? 1380 : 1040, 760);
        MinimumSize = ClientSize;
        Location = LauncherWindowPlacement.Place(Size, savedPosition, areas, Cursor.Position);
        BackColor = BackgroundColor;
        ForeColor = MainText;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        TrySetIcon();
        using (Stream? stream = typeof(LauncherForm).Assembly.GetManifestResourceStream("ForceBond.Launcher.peragus-backdrop.jpg"))
        {
            if (stream is not null) { using var image = Image.FromStream(stream); _backdrop = new Bitmap(image); }
        }

        Panel header = BuildHeader();
        Controls.Add(header);
        _playButton = BuildPlayButton();
        Controls.Add(BuildContent());
        Controls.Add(BuildFooter());
        if (_showPatchSidebar) {
            _patchLog.Location = new Point(1028, 112); _patchLog.Size = new Size(316, 608);
            Controls.Add(_patchLog);
        }
        L.Apply(this);

        _refreshTimer.Tick += async (_, _) => { RefreshEnvironment(); if (_startupComplete) await VerifyUpdatedResourcesAsync(); };
        Shown += async (_, _) =>
        {
            RefreshEnvironment();
            LauncherUpdates.Acknowledge(Environment.GetCommandLineArgs());
            if (!Environment.GetCommandLineArgs().Any(a => a.StartsWith("--preview", StringComparison.Ordinal)))
            {
                _working = true; _playButton.Enabled = false;
                if (!discordLaunch) {
                    if (LauncherUpdates.Source.Enabled) SetStatus("Checking for updates...", Teal);
                    await LauncherUpdates.CheckAsync(this, _shutdown.Token);
                }
                _accessButton.Visible = !BetaAccess.PublicMode;
                if (IsDisposed) return;
                _working = false;
                RefreshEnvironment();
                SetStatus(_sessionView.Description, _sessionView.Warning ? Gold : Teal);
                _startupComplete = true;
                await VerifyUpdatedResourcesAsync();
                if (IsDisposed) return;
                Location = LauncherWindowPlacement.Place(Size, Location, Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray(), Cursor.Position);
                if (discordLaunch) await LaunchCoopAsync(discordLaunch: true);
            }
            _refreshTimer.Start();
        };
        ResizeEnd += (_, _) => Location = LauncherWindowPlacement.Clamp(Size, Location, Screen.FromRectangle(Bounds).WorkingArea);
        FormClosing += (_, e) =>
        {
            if (_repairingResources) { e.Cancel = true; return; }
            if (!Environment.GetCommandLineArgs().Any(a => a.StartsWith("--preview", StringComparison.Ordinal)))
                try { LauncherWindowPlacement.Save(WindowPositionFile, WindowState == FormWindowState.Normal ? Location : RestoreBounds.Location); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LauncherUpdates.Log("Window position: " + ex.Message); }
            _shutdown.Cancel();
            _refreshTimer.Stop();
        };
        Disposed += (_, _) => { _windowIcon?.Dispose(); _refreshTimer.Dispose(); _shutdown.Dispose(); _backdrop?.Dispose(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_backdrop is not null)
        {
            using var attributes = new ImageAttributes();
            var tint = new ColorMatrix { Matrix33 = .34f }; attributes.SetColorMatrix(tint);
            e.Graphics.DrawImage(_backdrop, new Rectangle(0, 80, Width, Height - 80), 0, 0, _backdrop.Width, _backdrop.Height, GraphicsUnit.Pixel, attributes);
            using var shade = new LinearGradientBrush(new Rectangle(0, 80, Width, Height - 80), Color.FromArgb(225, BackgroundColor), Color.FromArgb(70, BackgroundColor), 0f);
            e.Graphics.FillRectangle(shade, 0, 80, Width, Height - 80);
            using var vignettePath = new GraphicsPath(); vignettePath.AddRectangle(new Rectangle(0, 80, Width, Height - 80));
            using var vignette = new PathGradientBrush(vignettePath) { CenterColor = Color.FromArgb(0, Color.Black), SurroundColors = [Color.FromArgb(165, Color.Black)], CenterPoint = new PointF(Width * .55f, Height * .48f) };
            e.Graphics.FillPath(vignette, vignettePath);
        }
        using var border = new Pen(Color.FromArgb(45, Teal), 1);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var topRule = new Pen(Gold, 2);
        e.Graphics.DrawLine(topRule, 0, 79, Width, 79);
        using var tealRule = new Pen(TealDim, 1);
        e.Graphics.DrawLine(tealRule, 36, 646, 1004, 646);
        using var glow = new SolidBrush(Color.FromArgb(10, Teal));
        e.Graphics.FillEllipse(glow, Width - 360, 90, 460, 460);
    }

    private Panel BuildHeader()
    {
        // Give anchored children the real width before adding them. A Dock=Top
        // panel starts at the default 200px width; anchoring controls against
        // that width pushed the window buttons outside the visible header.
        var header = new Panel { Dock = DockStyle.Top, Size = new Size(ClientSize.Width, 80), BackColor = Color.FromArgb(5, 10, 12) };
        try
        {
            using var stream = typeof(LauncherForm).Assembly.GetManifestResourceStream("ForceBond.Launcher.force-bond.png");
            if (stream is not null)
            {
                using var logo = Image.FromStream(stream);
                header.Controls.Add(new PictureBox
                {
                    Image = new Bitmap(logo), SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new Point(28, 15), Size = new Size(50, 50), BackColor = Color.Transparent
                });
            }
        }
        catch { }

        header.Controls.Add(new Label
        {
            Text = "STAR WARS  /  KNIGHTS OF THE OLD REPUBLIC II",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Gold,
            AutoSize = true, Location = new Point(94, 16), BackColor = Color.Transparent
        });
        header.Controls.Add(new Label
        {
            Text = "FORCE BOND",
            Font = new Font("Segoe UI Semibold", 21f, FontStyle.Bold), ForeColor = MainText,
            AutoSize = true, Location = new Point(92, 34), BackColor = Color.Transparent
        });
        _accessButton.Text = "BETA ACCESS";
        _accessButton.Location = new Point(516, 22); _accessButton.Size = new Size(136, 34);
        _accessButton.Animate = false; _accessButton.ForeColor = Teal; _accessButton.FillColor = PanelColor;
        _accessButton.BorderColor = TealDim; _accessButton.Cursor = Cursors.Hand;
        _accessButton.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _accessButton.Click += async (_, _) => {
            using var dialog = new BetaAccessWindow(); dialog.ShowDialog(this);
            if(dialog.CheckForUpdates)await LauncherUpdates.CheckAsync(this, _shutdown.Token);
            if(!IsDisposed)_accessButton.Visible = !BetaAccess.PublicMode;
        };
        header.Controls.Add(_accessButton);
        if (_showPatchSidebar) header.Controls.Add(new Label
        {
            Text = "STEAM CO-OP", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Teal,
            AutoSize = true, Location = new Point(ClientSize.Width - 370, 33), BackColor = Color.Transparent
        });
        else {
            var patches = new StyledButton { Text = "PATCH LOG", Location = new Point(664, 22), Size = new Size(136, 34), Animate = false,
                ForeColor = Teal, FillColor = PanelColor, BorderColor = TealDim, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            patches.Click += (_, _) => {
                using var dialog = new UtilityPatchLog(); dialog.ShowDialog(this);
            };
            header.Controls.Add(patches);
        }

        var minimize = MakeWindowButton("MINIMIZE", ClientSize.Width - 230, header, false);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var close = MakeWindowButton("CLOSE  ×", ClientSize.Width - 130, header, true);
        close.Click += (_, _) => Close();
        header.MouseDown += DragWindow;
        foreach (Control child in header.Controls)
            if (child is Label or PictureBox) child.MouseDown += DragWindow;
        return header;
    }

    private static Button MakeWindowButton(string text, int x, Control parent, bool isClose)
    {
        var button = new Button
        {
            Text = text, Location = new Point(x, 22), Size = new Size(90, 34),
            FlatStyle = FlatStyle.Flat, ForeColor = isClose ? Gold : Teal, BackColor = PanelColor,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold), Cursor = Cursors.Hand,
            AccessibleName = isClose ? "Close Force Bond launcher" : "Minimize Force Bond launcher",
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = isClose ? Color.FromArgb(112, 79, 49) : TealDim;
        button.FlatAppearance.MouseOverBackColor = isClose ? Color.FromArgb(73, 43, 37) : RaisedColor;
        parent.Controls.Add(button);
        return button;
    }

    private Control BuildContent()
    {
        var content = new Panel
        {
            Location = new Point(36, 112), Size = new Size(968, 510),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.Transparent
        };
        content.Controls.Add(new Label
        {
            Text = "EXPLORE THE GALAXY.\nTOGETHER.", AutoSize = false, Size = new Size(430, 110),
            Font = new Font("Segoe UI Semibold", 25f, FontStyle.Bold), ForeColor = MainText,
            Location = new Point(0, 0), BackColor = Color.Transparent
        });
        _sessionProgress.Location = new Point(438, 0);
        _sessionProgress.Size = new Size(530, 170);
        _sessionProgress.Font = new Font("Segoe UI", 8.25f);
        content.Controls.Add(_sessionProgress);
        var launchCard = MakeCard(new Point(0, 185), new Size(594, 282));
        launchCard.Controls.Add(MakeSectionLabel("GAME STATUS", new Point(24, 18)));
        AddStatusRow(launchCard, "STEAM CLIENT", _steamStatus, 55);
        AddStatusRow(launchCard, "GAME INSTALL", _gameStatus, 91);
        AddStatusRow(launchCard, "FORCE BOND", _runtimeStatus, 127);
        _pathLabel.Text = "Game folder not detected";
        _pathLabel.ForeColor = MutedText;
        _pathLabel.Font = new Font("Segoe UI", 8.5f);
        _pathLabel.AutoEllipsis = true;
        _pathLabel.Location = new Point(26, 163);
        _pathLabel.Size = new Size(525, 24);
        launchCard.Controls.Add(_pathLabel);

        _playButton.Location = new Point(24, 201);
        _playButton.Size = new Size(310, 54);
        _playButton.Click += async (_, _) => await LaunchCoopAsync();
        launchCard.Controls.Add(_playButton);
        var choose = new Button
        {
            Text = "CHANGE GAME FOLDER", Location = new Point(350, 210), Size = new Size(212, 38),
            FlatStyle = FlatStyle.Flat, ForeColor = Teal, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        choose.FlatAppearance.BorderColor = TealDim;
        choose.FlatAppearance.MouseOverBackColor = RaisedColor;
        choose.Click += (_, _) => ChooseGameFolder();
        launchCard.Controls.Add(choose);
        content.Controls.Add(launchCard);

        var infoCard = MakeCard(new Point(614, 185), new Size(354, 282));
        infoCard.Controls.Add(MakeSectionLabel("HOW TO CONNECT", new Point(22, 18)));
        AddStep(infoCard, "01", "START THE GAME", "Load your character or a save. Steam hosting starts when the world is ready.", 58);
        AddStep(infoCard, "02", "INVITE A FRIEND", "Invite or join your friend through Steam Friends.", 132);
        AddStep(infoCard, "03", "JOIN THE SESSION", "Accept the invite while KOTOR II is running.", 206);
        content.Controls.Add(infoCard);

        _status.Text = "Ready when you are. Steam Overlay handles the invite in game.";
        _status.AutoEllipsis = true;
        _status.ForeColor = MutedText;
        _status.Font = new Font("Segoe UI", 9f);
        _status.Location = new Point(2, 480);
        _status.Size = new Size(940, 22);
        content.Controls.Add(_status);
        return content;
    }

    private Control BuildFooter()
    {
        var footer = new Panel
        {
            Location = new Point(36, 658), Size = new Size(968, 62),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Transparent
        };
        footer.Controls.Add(new Label
        {
            Text = "ONE PARTY. ONE GALACTIC JOURNEY.",
            AutoSize = true, ForeColor = MutedText, Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Location = new Point(0, 12), BackColor = Color.Transparent
        });
        footer.Controls.Add(new Label
        {
            Text = $"Build {BuildVersion.Split('-')[0]}  /  {L.T("Closed Beta")}",
            AutoSize = false, ForeColor = Color.FromArgb(99, 119, 114), Font = new Font("Segoe UI", 8f),
            Location = new Point(0, 34), Size = new Size(258, 16), AutoEllipsis = true, BackColor = Color.Transparent
        });
        AddFooterAction(footer, "MOD MANAGER", 268, 126, ShowMods);
        AddFooterAction(footer, "SETTINGS", 400, 104, ShowSettings);
        AddFooterAction(footer, "DIAGNOSTICS", 510, 104, ShowDiagnostics);
        AddFooterAction(footer, "LOG", 620, 66, OpenGameLog);
        var socialTips = new ToolTip { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 5000 };
        footer.Disposed += (_, _) => socialTips.Dispose();
        AddSocialLink(footer, socialTips, SocialNetwork.Website, "Open project website", "https://force-bond.pages.dev", 734);
        AddSocialLink(footer, socialTips, SocialNetwork.Discord, "Join Force Bond on Discord", "https://discord.gg/9bPWjkaqBn", 790);
        AddSocialLink(footer, socialTips, SocialNetwork.Patreon, "Support us on Patreon", "https://www.patreon.com/cw/syndicate_corp", 846);
        AddSocialLink(footer, socialTips, SocialNetwork.YouTube, "Watch Syndicate Corp on YouTube", "https://www.youtube.com/@Syndicate_Corp", 902);
        return footer;
    }

    private static void AddFooterAction(Control parent, string title, int x, int width, Action action)
    {
        var button = new StyledButton { Text = title, Location = new Point(x, 12), Size = new Size(width, 34), Animate = false,
            FillColor = PanelColor, ForeColor = Teal, BorderColor = TealDim, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        button.Click += (_, _) => action(); parent.Controls.Add(button);
    }

    private void ShowMods()
    {
        if (_gamePath is null)
        {
            SetStatus("Use Change Game Folder to locate swkotor2.exe", Gold);
            return;
        }
        try
        {
            using var window = new ModsWindow(_gamePath);
            window.ShowDialog(this);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, ex.Message, L.T("MOD MANAGER"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSettings()
    {
        using var window = new SettingsWindow(LauncherSettings.Load(), VerifyFilesAsync);
        if (window.ShowDialog(this) == DialogResult.OK)
        {
            L.Language = LauncherSettings.Load().Language;
            L.Apply(this);
            _patchLog.Reload();
            RefreshEnvironment();
            SetStatus("Settings saved. Running sessions apply them automatically.", Teal);
        }
    }

    private async Task<string> VerifyFilesAsync()
    {
        if (_working) throw new InvalidOperationException("Another launcher operation is in progress.");
        ResolveInstallation();
        if (_gamePath is null) throw new InvalidOperationException("KOTOR II was not found. Use Change Game Folder to select its installation folder.");
        if (AnyGameProcess()) throw new InvalidOperationException("Close KOTOR II before verifying files.");
        _working = true; _repairingResources = true; _playButton.Enabled = false;
        string path = _gamePath;
        try
        {
            string report = await Task.Run(() => OdysseyEngine.Enabled ? NativeMigration.Run(path) + Environment.NewLine + OdysseyEngine.Verify(path) : InstallResources(path));
            VerifiedGameResources.Record(LauncherSettings.DataDirectory, BuildVersion, path);
            _verificationRequired = false; _verificationError = null;
            return report;
        }
        finally { _repairingResources = false; _working = false; if (!IsDisposed) RefreshEnvironment(); }
    }

    private async Task VerifyUpdatedResourcesAsync()
    {
        if (_working || IsDisposed || !_startupComplete) return;
        ResolveInstallation();
        if (_gamePath is null) return;
        _verificationRequired = VerifiedGameResources.NeedsVerification(LauncherSettings.DataDirectory, BuildVersion, _gamePath);
        if (!_verificationRequired) return;
        if (!string.Equals(_verificationAttemptPath, _gamePath, StringComparison.OrdinalIgnoreCase)) _verificationError = null;
        _verificationAttemptPath = _gamePath;
        _playButton.Enabled = false;
        if (_verificationError is not null) { SetStatus(_verificationError, Gold); return; }
        if (AnyGameProcess()) { SetStatus("Close KOTOR II to verify the updated mod files.", Gold); return; }
        SetStatus("Verifying updated mod files...", Teal);
        try
        {
            string report = await VerifyFilesAsync();
            LauncherUpdates.Log("POST_UPDATE_GAME_FILES_VERIFIED " + BuildVersion + " " + report.Replace(Environment.NewLine, "; "));
            if (!IsDisposed) SetStatus("Game files verified. Ready to launch.", Teal);
        }
        catch (Exception ex)
        {
            _verificationError = "Game file verification failed. Open Settings to retry.";
            LauncherUpdates.Log("POST_UPDATE_GAME_FILES_FAILED " + ex);
            if (!IsDisposed) SetStatus(_verificationError, Gold);
        }
    }

    private void ShowDiagnostics()
    {
        using var window = new DiagnosticsWindow(BuildDiagnosticReport(), OpenGameLog);
        window.ShowDialog(this);
    }

    private void OpenGameLog()
    {
        try
        {
            if (_lastLogPath is null || !File.Exists(_lastLogPath)) { SetStatus("No game log yet. Launch Force Bond first.", Gold); return; }
            Process.Start(new ProcessStartInfo(_lastLogPath) { UseShellExecute = true });
        }
        catch (Exception ex) { SetStatus($"Could not open the log: {ex.Message}", Gold); }
    }

    private string BuildDiagnosticReport()
    {
        var report = new StringBuilder();
        report.AppendLine(L.T("FORCE BOND / SESSION DIAGNOSTICS"));
        report.AppendLine($"{L.T("Launcher build:")} {BuildVersion}");
        report.AppendLine("AutoUpdates=" + (BetaAccess.Configured ? "ACCESS_SERVICE" : "ACCESS_SERVICE_NOT_CONNECTED"));
        report.AppendLine("DownloadAccess=" + (BetaAccess.PublicMode ? "PUBLIC" : "DISCORD_CLOSED_BETA"));
        report.AppendLine("GameFilesVerification=" + (_verificationRequired ? "PENDING" : "COMPLETE_OR_NOT_REQUIRED"));
        if (_verificationError is not null) report.AppendLine("VerificationDetail=" + _verificationError);
        report.AppendLine($"{L.T("Captured UTC:")} {DateTime.UtcNow:O}");
        report.AppendLine($"{L.T("Game folder:")} {_gamePath ?? L.T("Not detected")}");
        report.AppendLine($"{L.T("Game process:")} {_gamePid?.ToString() ?? L.T("Not running")}");
        report.AppendLine($"{L.T("Session:")} {L.T(_sessionView.RuntimeText)}");
        report.AppendLine($"{L.T("Detail:")} {L.T(_sessionView.Description)}");
        report.AppendLine($"{L.T("Settings:")} {LauncherSettings.FilePath}");
        report.AppendLine($"{L.T("Log:")} {_lastLogPath ?? L.T("No runtime log in this launcher session")}");
        report.AppendLine(); report.AppendLine(L.T("LIVE RUNTIME STATUS"));
        if (_session is null) report.AppendLine(L.T("No fresh status for the current game process."));
        else foreach (var field in _session.Fields) report.AppendLine($"{field.Key}={field.Value}");
        report.AppendLine(); report.AppendLine(L.T("RECENT RUNTIME LOG"));
        try
        {
            if (_lastLogPath is not null && File.Exists(_lastLogPath))
            {
                report.AppendLine(RuntimeLogReader.ReadTail(_lastLogPath));
            }
            else report.AppendLine(L.T("No log available."));
        }
        catch (Exception ex) { report.AppendLine(L.T($"Log could not be read: {ex.Message}")); }
        return report.ToString();
    }

    private void AddSocialLink(Control parent, ToolTip tips, SocialNetwork network, string description, string url, int x)
    {
        var button = new SocialLinkButton(network)
        {
            Location = new Point(x, 8), Size = new Size(44, 44),
            AccessibleName = description, AccessibleDescription = description,
            TabStop = true
        };
        tips.SetToolTip(button, description);
        // Updating text during Popup re-enters native tooltip text shaping.
        // Localize before the hover timer can open the tooltip instead.
        button.MouseEnter += (_, _) => {
            string description = button.AccessibleDescription ?? string.Empty;
            if (tips.GetToolTip(button) != description) tips.SetToolTip(button, description);
        };
        button.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus($"Could not open link: {ex.Message}", Gold); }
        };
        parent.Controls.Add(button);
    }

    private static Panel MakeCard(Point location, Size size)
    {
        var panel = new Panel { Location = location, Size = size, BackColor = PanelColor };
        panel.ControlAdded += (_, e) => {
            if (e.Control is Label label && label.BackColor == PanelColor)
                label.BackColor = Color.Transparent;
        };
        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new LinearGradientBrush(panel.ClientRectangle,
                Color.FromArgb(15, 34, 35), PanelColor, 90f);
            e.Graphics.FillRectangle(fill, panel.ClientRectangle);
            using var pen = new Pen(Color.FromArgb(105, TealDim), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            using var inner = new Pen(Color.FromArgb(14, Teal), 1);
            e.Graphics.DrawRectangle(inner, 2, 2, panel.Width - 5, panel.Height - 5);
            using var accent = new Pen(Gold, 2);
            e.Graphics.DrawLine(accent, 0, 1, 72, 1);
        };
        return panel;
    }

    private static Label MakeSectionLabel(string text, Point location) => new()
    {
        Text = text, AutoSize = true, Location = location,
        ForeColor = Gold, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
    };

    private void AddStatusRow(Control parent, string caption, Label value, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = caption, Location = new Point(26, y), AutoSize = true,
            ForeColor = MutedText, Font = new Font("Segoe UI", 8f, FontStyle.Bold)
        });
        value.Text = "CHECKING";
        value.Location = new Point(190, y - 1);
        value.Size = new Size(340, 22);
        value.ForeColor = Teal;
        value.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        parent.Controls.Add(value);
    }

    private static void AddStep(Control parent, string number, string title, string body, int y)
    {
        var badge = new Label
        {
            Text = number, Location = new Point(22, y), Size = new Size(36, 34),
            ForeColor = Teal, BackColor = RaisedColor, TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        parent.Controls.Add(badge);
        parent.Controls.Add(new Label
        {
            Text = title, Location = new Point(70, y - 1), Size = new Size(245, 18),
            ForeColor = MainText, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        });
        parent.Controls.Add(new Label
        {
            Text = body, Location = new Point(70, y + 19), Size = new Size(252, 44),
            ForeColor = MutedText, Font = new Font("Segoe UI", 8f)
        });
    }

    private StyledButton BuildPlayButton() => new()
    {
        Text = "LAUNCH FORCE BOND", FillColor = Teal, ForeColor = Color.FromArgb(4, 17, 17),
        Font = new Font("Segoe UI", 10f, FontStyle.Bold), Cursor = Cursors.Hand,
        BorderColor = Teal, Animate = true
    };

    private void RefreshEnvironment()
    {
        try
        {
            ResolveInstallation();
            Process[] steamProcesses = Process.GetProcessesByName("steam");
            bool steamRunning = steamProcesses.Length > 0;
            foreach (var steam in steamProcesses) steam.Dispose();
            _steamStatus.Text = steamRunning ? "STEAM RUNNING" : _steamExe is not null && File.Exists(_steamExe) ? "STEAM INSTALLED / CLOSED" : "STEAM NOT FOUND";
            _steamStatus.ForeColor = _steamExe is not null ? Teal : Gold;
            _gameStatus.Text = _gamePath is not null ? "KOTOR II FOUND" : "SELECT GAME FOLDER";
            _gameStatus.ForeColor = _gamePath is not null ? Teal : Gold;
            _pathLabel.Text = _gamePath ?? "Use Change Game Folder to locate swkotor2.exe";
            using Process? runningProcess = FindGameProcess();
            bool gameJustClosed = _gamePid.HasValue && runningProcess is null;
            _gamePid = runningProcess?.Id;
            if (gameJustClosed && !_working) SetStatus("Game closed. Ready to launch.", Teal);
            _session = null;
            if (runningProcess is not null)
            {
                _lastLogPath = OdysseyEngine.Enabled ? Path.Combine(LauncherSettings.DataDirectory, "OdysseyMP", LauncherSettings.Load().EngineMode, "engine.log") : Path.Combine(Path.GetTempPath(), $"OdysseyMP-coop-{runningProcess.Id}.log");
                try { _session = SessionState.Read(runningProcess.Id, runningProcess.StartTime.ToUniversalTime().ToFileTimeUtc()); }
                catch (System.ComponentModel.Win32Exception) { }
            }
            bool active = runningProcess is not null && (OdysseyEngine.Enabled || IsRuntimeLoaded(runningProcess) || _session is not null);
            _sessionView = SessionView.Create(runningProcess is not null, active, _session);
            _sessionProgress.UpdateState(_sessionView, _session);
            _playButton.Text = active
                ? "GAME RUNNING"
                : runningProcess is null ? "LAUNCH FORCE BOND" : "CONNECT FORCE BOND";
            _playButton.Enabled = !_working && !_verificationRequired && !active;
            _runtimeStatus.Text = _sessionView.RuntimeText;
            _runtimeStatus.ForeColor = _sessionView.Warning ? Gold : Teal;
            if (OdysseyEngine.Enabled && !OdysseyEngine.Available) {
                _runtimeStatus.Text = "ENGINE FILES MISSING"; _runtimeStatus.ForeColor = Gold;
                _playButton.Enabled = false;
                SetStatus("Extract the complete package, including the Engine folder. Do not copy only ForceBond.exe.", Gold);
            }
        }
        catch (Exception ex)
        {
            _gameStatus.Text = "CHECK INSTALLATION";
            _gameStatus.ForeColor = Gold;
            _status.Text = ex.Message;
        }
    }

    private async Task LaunchCoopAsync(bool discordLaunch = false)
    {
        if (_working) return;
        _working = true;
        _playButton.Enabled = false;
        Process? game = null;
        using var launchMutex = new Mutex(false, @"Local\ForceBond.OdysseyMP.GameLaunch");
        bool ownsLaunch = false;
        try
        {
            SetStatus("Checking Steam and game files...", Teal);
            ResolveInstallation();
            if (_gamePath is null) throw new InvalidOperationException("KOTOR II was not found. Use Change Game Folder to select its installation folder.");
            if (_steamExe is null || !File.Exists(_steamExe)) throw new InvalidOperationException("Steam was not found. Install or launch the Steam client first.");
            try { ownsLaunch = launchMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsLaunch = true; }
            if (!ownsLaunch) throw new InvalidOperationException("Force Bond is already starting. Please wait for the game window.");

            if (OdysseyEngine.Enabled) {
                var selected = LauncherSettings.Load();
                if (discordLaunch && !selected.DiscordEnabled)
                    throw new InvalidOperationException("Enable Discord integration in Settings, then accept the invitation again.");
                var steamProcesses = Process.GetProcessesByName("steam");
                bool steamRunning = steamProcesses.Length > 0;
                foreach (var process in steamProcesses) process.Dispose();
                if (!steamRunning) throw new InvalidOperationException("Start Steam, then launch Force Bond or accept the invitation again.");
                game = OdysseyEngine.FindRunning();
                if (game is null) {
                    if (AnyGameProcess()) throw new InvalidOperationException("Close the original KOTOR II before starting OdysseyMP.");
                    _lastLogPath = Path.Combine(LauncherSettings.DataDirectory, "OdysseyMP", selected.EngineMode, "engine.log");
                    game = OdysseyEngine.Start(_gamePath, selected);
                    await WaitForMainWindowAsync(game, _shutdown.Token);
                }
                SetStatus(discordLaunch ? "Force Bond started. Waiting for the Discord invitation..." : "OdysseyMP started. Load your character, then invite through Steam (F8).", Teal);
                return;
            }
            game = FindGameProcess();

            InstallResources(_gamePath);
            VerifiedGameResources.Record(LauncherSettings.DataDirectory, BuildVersion, _gamePath);
            if (game is null)
            {
                GameDisplay.Prepare(_gamePath, LauncherSettings.Load().BorderlessFullscreen);
                SetStatus("Launching KOTOR II through Steam...", Teal);
                Process.Start(new ProcessStartInfo(_steamExe, "-applaunch 208580") { UseShellExecute = false });
                game = await WaitForGameAsync(_shutdown.Token);
            }
            else
            {
                GameDisplay.RequireWindowedForRunningGame(_gamePath, LauncherSettings.Load().BorderlessFullscreen);
                SetStatus("KOTOR II is already running. Connecting Force Bond...", Teal);
            }

            if (game is null) throw new TimeoutException("KOTOR II did not start. Launch it through Steam, then try again.");
            await WaitForMainWindowAsync(game, _shutdown.Token);
            if (IsRuntimeLoaded(game))
            {
                SetStatus("Runtime loaded. Load Peragus - Administration Level and watch the session status.", Teal);
                _playButton.Text = "GAME RUNNING";
                return;
            }

            string support = Path.Combine(AppContext.BaseDirectory, "Support");
            string runtime = Path.Combine(support, "OdysseyMPCoop.dll");
            if (!File.Exists(runtime))
                throw new FileNotFoundException("Force Bond support files are missing. Reinstall the launcher package.");

            SetStatus("Connecting Force Bond to the game...", Teal);
            await Task.Run(() => GameInjector.Inject(game, runtime), _shutdown.Token);

            _playButton.Text = "GAME RUNNING";
            SetStatus("Runtime loaded. Load Peragus - Administration Level; invite through Steam Friends.", Teal);
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) SetStatus("Launch cancelled.", Gold);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && IsHandleCreated) SetStatus(ex.Message, Gold);
        }
        finally
        {
            if (ownsLaunch) launchMutex.ReleaseMutex();
            game?.Dispose();
            if (!IsDisposed && IsHandleCreated)
            {
                _working = false;
                _playButton.Enabled = true;
                RefreshEnvironment();
            }
        }
    }

    private void ResolveInstallation()
    {
        string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForceBond", "settings.ini");
        if (File.Exists(settings))
        {
            string? saved = File.ReadAllText(settings).Trim();
            if (!string.IsNullOrWhiteSpace(saved) && File.Exists(Path.Combine(saved, "swkotor2.exe")))
                _gamePath = Path.GetFullPath(saved);
        }

        string? steamPath = null;
        string? steamExe = null;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            steamPath = key?.GetValue("SteamPath") as string;
            steamExe = key?.GetValue("SteamExe") as string;
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        if (steamPath is not null) steamPath = steamPath.Replace('/', Path.DirectorySeparatorChar);
        if (steamExe is not null) steamExe = steamExe.Replace('/', Path.DirectorySeparatorChar);
        if (steamExe is null || !File.Exists(steamExe))
        {
            string? candidate = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            }.Where(x => !string.IsNullOrWhiteSpace(x))
             .Select(x => Path.Combine(x!, "Steam", "steam.exe"))
             .FirstOrDefault(File.Exists);
            steamExe = candidate;
        }
        _steamExe = steamExe;

        if (_gamePath is not null && File.Exists(Path.Combine(_gamePath, "swkotor2.exe"))) return;
        _gamePath = null;
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(steamPath)) libraries.Add(steamPath);
        if (!string.IsNullOrWhiteSpace(steamExe)) libraries.Add(Path.GetDirectoryName(steamExe)!);
        foreach (string root in libraries.ToArray())
        {
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            string content = File.ReadAllText(vdf);
            foreach (Match match in Regex.Matches(content, "\"path\"\\s*\"((?:\\\\.|[^\"])*?)\""))
            {
                string path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path)) libraries.Add(path);
            }
        }
        foreach (string root in libraries)
        {
            string manifest = Path.Combine(root, "steamapps", "appmanifest_208580.acf");
            if (!File.Exists(manifest)) continue;
            string candidate = Path.Combine(root, "steamapps", "common", "Knights of the Old Republic II");
            if (File.Exists(Path.Combine(candidate, "swkotor2.exe")))
            {
                _gamePath = candidate;
                break;
            }
        }
    }

    private string InstallResources(string gamePath)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Support", "OverrideAssets");
        string overridePath = Path.Combine(gamePath, "Override");
        string modulesPath = Path.Combine(gamePath, "Modules");
        string moduleTarget = Path.Combine(modulesPath, "101PER.mod");
        string catalogSource = Path.Combine(source, "globalcat.2da");
        string catalogTarget = Path.Combine(overridePath, "globalcat.2da");
        if (!File.Exists(catalogSource)) throw new FileNotFoundException("Required Force Bond global catalog is missing.");
        byte[]? oldCatalog = File.Exists(catalogTarget) ? File.ReadAllBytes(catalogTarget) : null;
        byte[] catalog = GlobalCatalog.Merge(File.ReadAllBytes(catalogSource), oldCatalog);
        bool catalogChanged = oldCatalog is null || !catalog.AsSpan().SequenceEqual(oldCatalog);
        string[] names = ["101per.are", "odmp_mirror.ncs", "odmp_module_load.ncs", "odmp_module_init.ncs", "odmp_bridge_init.ncs", "odmp_mirror_tick.ncs", "odmp_chat_tick.ncs", "odmp_party.ncs", "odmp_world.ncs", "odmp_life_clean.ncs", "odmp_enemy_tick.ncs", "odmp_enemy_dmg.ncs", "odmp_rep_mode.ncs", "odmp_rev_click.ncs", "odmp_rev_talk.ncs", "odmp_revive.dlg", "odmp_te0.utc", "odmp_te1.utc", "odmp_test.utc", "a_doormor.ncs", "a_setmor.ncs", "a_doormedsto.ncs", "odmp_old_med.ncs", "k_xp60.ncs", "odmp_old_xp60.ncs", "odmp_old_mor.ncs", "odmp_old_setmor.ncs", "a_fadeonload.ncs", "a_hatchopen.ncs", "odmp_old_fade.ncs", "odmp_old_hatch.ncs", "odmp_mirror.utc"];
        names = names.Concat(Enumerable.Range(0, 12).Select(i => $"odmp_c{i}.utc")).ToArray();
        names = names.Concat(Enumerable.Range(0, 12).Select(i => $"odmp_d{i}.utc")).ToArray();
        names = names.Concat(Enumerable.Range(0, 64).Select(i => $"odmp_p{i}.utc")).ToArray();
        string[] headTemplates = Directory.GetFiles(source, "odmp_h*.utc")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        if (headTemplates.Length != 256)
            throw new InvalidOperationException($"Expected 256 partner head templates, found {headTemplates.Length}. Rebuild the Force Bond package.");
        names = names.Concat(headTemplates).ToArray();
        string[] equipmentTemplates = Directory.GetFiles(source, "odmpi*.uti")
            .Select(path => Path.GetFileName(path)!).ToArray();
        if (equipmentTemplates.Length == 0)
            throw new InvalidOperationException("Cosmetic equipment templates are missing. Extract the complete Force Bond package.");
        names = names.Concat(equipmentTemplates).Append("odmp_items.2da").ToArray();
        foreach (string name in names)
            if (!File.Exists(Path.Combine(source, name))) throw new FileNotFoundException($"Required game resource is missing: {name}");
        const string legacyModuleHash = "9432F6FE9F229AFC185C4EA58A4CC75D79AC79D268EE572D27F07B2697C686DF";
        bool legacyModule = File.Exists(moduleTarget) &&
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(moduleTarget))) == legacyModuleHash;
        if (File.Exists(moduleTarget) && !legacyModule)
            throw new InvalidOperationException("A different Modules\\101PER.mod is installed. Force Bond will preserve it; resolve compatibility before installing this update.");

        var changed = names.Where(name => !FilesEqual(Path.Combine(source, name), Path.Combine(overridePath, name))).ToArray();
        string[] legacyBackups = Directory.Exists(overridePath)
            ? Directory.GetDirectories(overridePath).Where(path => Regex.IsMatch(Path.GetFileName(path), @"^(ForceBond|OdysseyMP)-backup-\d{8}-\d{6}-[a-fA-F0-9]{6,32}$")).ToArray()
            : [];
        string[] obsolete = new[] { "odmp_orig_fadeonload.ncs", "odmp_orig_hatchopen.ncs" }
            .Where(name => File.Exists(Path.Combine(overridePath, name))).ToArray();
        string Report(string? backupPath) => string.Join(Environment.NewLine,
            L.T("Files checked: {0}").Replace("{0}", (names.Length + 1).ToString()),
            L.T("Files restored: {0}").Replace("{0}", (changed.Length + (catalogChanged ? 1 : 0)).ToString()),
            L.T("Old files archived: {0}").Replace("{0}", (obsolete.Length + (legacyModule ? 1 : 0)).ToString()),
            L.T("Backup folders relocated: {0}").Replace("{0}", legacyBackups.Length.ToString()),
            backupPath is null ? L.T("All Force Bond files are up to date.") : L.T("Backup folder: {0}").Replace("{0}", backupPath));
        if (changed.Length == 0 && !legacyModule && !catalogChanged && legacyBackups.Length == 0 && obsolete.Length == 0) return Report(null);
        if (AnyGameProcess())
            throw new InvalidOperationException("Close KOTOR II once so Force Bond can install its game resources. Then launch the game from here.");

        foreach (string name in names.Append("globalcat.2da").Concat(obsolete))
        {
            string target = Path.Combine(overridePath, name);
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A mod resource is a symbolic link; repair was stopped.");
        }
        Directory.CreateDirectory(overridePath);
        string backupRoot = Path.GetFullPath(Path.Combine(gamePath, "ForceBond-backups"));
        if ((File.GetAttributes(overridePath) & FileAttributes.ReparsePoint) != 0 ||
            (Directory.Exists(backupRoot) && (File.GetAttributes(backupRoot) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Backup migration requires ordinary directories, not junctions or symbolic links.");
        Directory.CreateDirectory(backupRoot);
        string checkedOverride = Path.GetFullPath(overridePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string directory in legacyBackups)
        {
            string from = Path.GetFullPath(directory);
            string destination = Path.GetFullPath(Path.Combine(backupRoot, Path.GetFileName(from) + "-" + Guid.NewGuid().ToString("N")));
            if (!from.StartsWith(checkedOverride, StringComparison.OrdinalIgnoreCase) ||
                !destination.StartsWith(backupRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Unsafe backup migration path; no directory was moved.");
            Directory.Move(from, destination);
        }
        string[] existing = changed.Where(name => File.Exists(Path.Combine(overridePath, name)))
            .Concat(catalogChanged && oldCatalog is not null ? ["globalcat.2da"] : Array.Empty<string>()).ToArray();
        string? backup = null;
        if (existing.Length > 0 || legacyModule || obsolete.Length > 0)
        {
            backup = Path.Combine(backupRoot, $"ForceBond-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(backup);
            foreach (string name in existing) File.Copy(Path.Combine(overridePath, name), Path.Combine(backup, name));
        }
        foreach (string name in obsolete)
        {
            string oldPath = Path.Combine(overridePath, name);
            if ((File.GetAttributes(oldPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A mod resource is a symbolic link; repair was stopped.");
            File.Move(oldPath, Path.Combine(backup!, name));
        }
        foreach (string name in changed)
        {
            string destination = Path.Combine(overridePath, name);
            string temporary = destination + ".forcebond-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(Path.Combine(source, name), temporary); File.Move(temporary, destination, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (catalogChanged)
        {
            string temporary = catalogTarget + ".forcebond-" + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, catalog); File.Move(temporary, catalogTarget, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        if (legacyModule)
        {
            // Exact hash of our old one-resource module. Preserve it outside the
            // Modules folder so it cannot shadow stock RIM/dialogue resources.
            File.Move(moduleTarget, Path.Combine(backup!, "101PER.mod"));
        }
        return Report(backup ?? backupRoot);
    }

    private static bool FilesEqual(string first, string second)
    {
        if (!File.Exists(first) || !File.Exists(second)) return false;
        using var left = SHA256.Create();
        using var right = SHA256.Create();
        return left.ComputeHash(File.ReadAllBytes(first)).AsSpan().SequenceEqual(right.ComputeHash(File.ReadAllBytes(second)));
    }

    private static bool AnyGameProcess()
    {
        Process[] games = Process.GetProcessesByName("swkotor2").Concat(Process.GetProcessesByName("OdysseyMP")).ToArray();
        bool found = games.Length != 0;
        foreach (Process game in games) game.Dispose();
        return found;
    }

    private Process? FindGameProcess()
    {
        if (OdysseyEngine.Enabled) return OdysseyEngine.FindRunning();
        if (_gamePath is null) return null;
        string expected = Path.GetFullPath(Path.Combine(_gamePath, "swkotor2.exe"));
        foreach (Process process in Process.GetProcessesByName("swkotor2"))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) return process;
            }
            catch { }
            process.Dispose();
        }
        return null;
    }

    private async Task<Process?> WaitForGameAsync(CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            Process? process = FindGameProcess();
            if (process is not null) return process;
            await Task.Delay(1000, token);
        }
        return null;
    }

    private static async Task WaitForMainWindowAsync(Process process, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited) throw new InvalidOperationException("KOTOR II closed before it was ready.");
            if (process.MainWindowHandle != IntPtr.Zero) return;
            await Task.Delay(500, token);
        }
        throw new TimeoutException("KOTOR II did not show a game window. Wait for the main menu and try again.");
    }

    private static bool IsRuntimeLoaded(Process process)
    {
        try { return process.Modules.Cast<ProcessModule>().Any(module => module.ModuleName.Equals("OdysseyMPCoop.dll", StringComparison.OrdinalIgnoreCase)); }
        catch { return false; }
    }

    private void ChooseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.T("Select the Knights of the Old Republic II installation folder (the folder containing swkotor2.exe)."),
            UseDescriptionForTitle = true,
            SelectedPath = _gamePath ?? string.Empty,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!File.Exists(Path.Combine(dialog.SelectedPath, "swkotor2.exe")))
        {
            SetStatus("That folder does not contain swkotor2.exe.", Gold);
            return;
        }
        _gamePath = Path.GetFullPath(dialog.SelectedPath);
        string settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForceBond", "settings.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, _gamePath);
        RefreshEnvironment();
        SetStatus("Game folder saved.", Teal);
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
    }

    private void TrySetIcon()
    {
        try
        {
            _windowIcon = new LauncherWindowIcon(this);
        }
        catch (Exception ex) { LauncherUpdates.Log("Window icon: " + ex.Message); }
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
}

internal enum SocialNetwork { Website, Discord, Patreon, YouTube }

internal sealed class SocialLinkButton : Control
{
    private static readonly Image DiscordLogo = LoadDiscordLogo();
    private static readonly Image PatreonLogo = LoadPatreonLogo();
    private readonly SocialNetwork _network;

    public SocialLinkButton(SocialNetwork network)
    {
        _network = network;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
        Color borderColor = hovered ? Color.FromArgb(48, 210, 181) : Color.FromArgb(35, 91, 84);
        using (var fill = new SolidBrush(hovered ? Color.FromArgb(19, 45, 44) : Color.FromArgb(9, 22, 24)))
        using (var border = new Pen(borderColor, hovered ? 1.6f : 1f))
        using (var tile = RoundedRect(new Rectangle(2, 2, Width - 5, Height - 5), 7))
        {
            g.FillPath(fill, tile);
            g.DrawPath(border, tile);
        }

        switch (_network)
        {
            case SocialNetwork.Website: DrawWebsite(g); break;
            case SocialNetwork.Discord: DrawDiscord(g); break;
            case SocialNetwork.Patreon: DrawPatreon(g); break;
            case SocialNetwork.YouTube: DrawYouTube(g); break;
        }
    }

    private static void DrawWebsite(Graphics g)
    {
        using var pen = new Pen(Color.FromArgb(48, 210, 181), 1.7f);
        g.DrawEllipse(pen, 9, 9, 26, 26); g.DrawEllipse(pen, 16, 9, 12, 26);
        g.DrawLine(pen, 9, 22, 35, 22);
        g.DrawArc(pen, 10, 12, 24, 10, 0, 180); g.DrawArc(pen, 10, 22, 24, 10, 180, 180);
    }

    private void DrawDiscord(Graphics g)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(DiscordLogo, new Rectangle(7, 7, 30, 30));
    }

    private void DrawPatreon(Graphics g)
    {
        var tint = new ColorMatrix
        {
            Matrix00 = 0, Matrix11 = 0, Matrix22 = 0,
            Matrix40 = 1f, Matrix41 = 66f / 255f, Matrix42 = 77f / 255f
        };
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(tint);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(PatreonLogo, new Rectangle(7, 7, 30, 30), 0, 0, PatreonLogo.Width, PatreonLogo.Height, GraphicsUnit.Pixel, attributes);
    }

    private static Image LoadPatreonLogo() => LoadImage("ForceBond.Launcher.patreon-logo.png");
    private static Image LoadDiscordLogo() => LoadImage("ForceBond.Launcher.discord-logo.png");

    private static Image LoadImage(string resourceName)
    {
        using Stream stream = typeof(SocialLinkButton).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded image resource is missing: {resourceName}");
        return new Bitmap(stream);
    }

    private void DrawYouTube(Graphics g)
    {
        using var brand = new SolidBrush(Color.FromArgb(255, 0, 51));
        using var tile = RoundedRect(new Rectangle(7, 12, 30, 20), 6);
        g.FillPath(brand, tile);
        using var play = new SolidBrush(Color.White);
        Point[] triangle = [new Point(19, 17), new Point(19, 27), new Point(27, 22)];
        g.FillPolygon(play, triangle);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}



