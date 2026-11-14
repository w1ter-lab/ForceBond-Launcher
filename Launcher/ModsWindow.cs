using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using ForceBond.Updates;

namespace ForceBond.Launcher;

internal sealed record ManagedMod(string Path, string Name, string Source, bool Enabled)
{
    internal ulong? WorkshopId => Source == "Steam Workshop" && ulong.TryParse(System.IO.Path.GetFileName(Path), out ulong id) ? id : null;
}

internal static class ModLibrary
{
    private static string StateFile => Path.Combine(LauncherSettings.DataDirectory, "mods.json");

    internal static List<ManagedMod> Load(string gamePath)
    {
        UpdateFiles.NoLinks(StateFile);
        var saved = File.Exists(StateFile)
            ? JsonSerializer.Deserialize<List<ManagedMod>>(File.ReadAllText(StateFile)) ?? []
            : [];
        var result = saved.Where(mod => mod.Source == "Nexus Mods").ToList();
        var steamApps = Directory.GetParent(gamePath)?.Parent;
        if (steamApps?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true)
        {
            string workshop = Path.Combine(steamApps.FullName, "workshop", "content", "208580");
            if (Directory.Exists(workshop))
            {
                UpdateFiles.NoLinks(workshop);
                foreach (string folder in Directory.EnumerateDirectories(workshop).Order(StringComparer.OrdinalIgnoreCase))
                {
                    string id = Path.GetFileName(folder);
                    if (!ulong.TryParse(id, out _) || !IsPackage(folder)) continue;
                    var prior = saved.FirstOrDefault(mod => mod.Source == "Steam Workshop" &&
                        string.Equals(mod.Path, folder, StringComparison.OrdinalIgnoreCase));
                    result.Add(new(folder, prior?.Name ?? "Workshop #" + id, "Steam Workshop", prior?.Enabled ?? false));
                }
            }
        }
        var order = saved.Select((mod, index) => (mod.Path, index))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        return result.OrderBy(mod => order.GetValueOrDefault(mod.Path, int.MaxValue)).ToList();
    }

    internal static bool IsPackage(string folder) =>
        Directory.Exists(Path.Combine(folder, "Override")) ||
        Directory.Exists(Path.Combine(folder, "Modules")) ||
        Directory.Exists(Path.Combine(folder, "Lips"));

    internal static void Save(List<ManagedMod> mods, string gamePath)
    {
        var previous = File.Exists(StateFile)
            ? JsonSerializer.Deserialize<List<ManagedMod>>(File.ReadAllText(StateFile)) ?? []
            : [];
        string? steamApps = Directory.GetParent(gamePath)?.Parent?.FullName;
        string prefix = steamApps is null ? "" : Path.Combine(steamApps, "workshop", "content", "208580") + Path.DirectorySeparatorChar;
        mods = [.. mods, .. previous.Where(mod => mod.Source == "Steam Workshop" &&
            (prefix.Length == 0 || !mod.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))];
        UpdateFiles.NoLinks(StateFile);
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        string pending = StateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(mods));
            File.Move(pending, StateFile, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}

internal sealed class ModsWindow : UtilityWindow
{
    private readonly string _gamePath;
    private readonly List<ManagedMod> _mods;
    private readonly CancellationTokenSource _titleLookup = new();
    private static readonly Color ListBackground = Color.FromArgb(12, 30, 31);
    private static readonly Color SelectionBackground = Color.FromArgb(22, 63, 58);
    private readonly Label _status = new() { Location = new Point(24, 418), Size = new Size(672, 26), AutoEllipsis = true };
    private readonly ListView _list = new()
    {
        View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, MultiSelect = false, ShowItemToolTips = true,
        BackColor = ListBackground, ForeColor = Ink, OwnerDraw = true, BorderStyle = BorderStyle.None,
        Location = new Point(24, 138), Size = new Size(672, 270)
    };

    internal ModsWindow(string gamePath) : base(L.T("MOD MANAGER"), new Size(720, 502))
    {
        _gamePath = gamePath;
        _mods = ModLibrary.Load(gamePath);
        Caption(L.T("Right-click a mod to enable or disable it. Applies on next launch."), 72);
        _list.Columns.Add(L.T("Mod"), 352);
        _list.Columns.Add(L.T("Source"), 157);
        _list.Columns.Add(L.T("Status"), 137);
        var header = new Panel
        {
            Location = new Point(24, 114),
            Size = new Size(672, 24),
            BackColor = SelectionBackground
        };
        header.Paint += (_, e) =>
        {
            int x = 0;
            foreach (ColumnHeader column in _list.Columns)
            {
                DrawCell(e.Graphics, column.Text, new Rectangle(x, 0, column.Width, header.Height), Gold);
                x += column.Width;
            }
        };
        Controls.Add(header);
        _list.DrawSubItem += (_, e) =>
        {
            using var fill = new SolidBrush(e.Item?.Selected == true ? SelectionBackground : ListBackground);
            e.Graphics.FillRectangle(fill, e.Bounds);
            DrawCell(e.Graphics, e.SubItem?.Text ?? "", e.Bounds, e.SubItem?.ForeColor ?? Ink);
            using var line = new Pen(Color.FromArgb(25, 56, 55));
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || _list.GetItemAt(e.X, e.Y) is not { Tag: int index } ||
                _mods[index].WorkshopId is not ulong id) return;
            RunAction(() => Process.Start(new ProcessStartInfo(WorkshopTitles.Page(id)) { UseShellExecute = true })?.Dispose());
        };
        Controls.Add(_list);
        Controls.Add(_status);

        var menu = new ContextMenuStrip { BackColor = ListBackground, ForeColor = Ink, ShowImageMargin = false };
        var toggle = new ToolStripMenuItem();
        menu.Items.Add(toggle);
        menu.Opening += (_, e) =>
        {
            e.Cancel = _list.SelectedItems.Count != 1;
            if (!e.Cancel)
            {
                int index = (int)_list.SelectedItems[0].Tag!;
                toggle.Text = L.T(_mods[index].Enabled ? "Disable" : "Enable");
            }
        };
        toggle.Click += (_, _) =>
        {
            if (_list.SelectedItems.Count != 1) return;
            int index = (int)_list.SelectedItems[0].Tag!;
            if (!_mods[index].Enabled && !ModLibrary.IsPackage(_mods[index].Path))
            {
                MessageBox.Show(this, L.T("Select a separate mod folder containing Override, Modules or Lips."), Text);
                return;
            }
            var next = new List<ManagedMod>(_mods);
            next[index] = next[index] with { Enabled = !next[index].Enabled };
            Commit(next);
        };
        _list.ContextMenuStrip = menu;
        _list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            _list.SelectedItems.Clear();
            if (_list.GetItemAt(e.X, e.Y) is { } item) item.Selected = true;
        };
        Disposed += (_, _) => menu.Dispose();

        AddAction("ADD NEXUS MOD FOLDER", 24, 233, AddPackage);
        AddAction("COPY MOD LIST", 273, 190, CopyList);
        AddAction("PASTE MOD LIST", 479, 217, PasteList);
        FormClosed += (_, _) => _titleLookup.Cancel();
        Disposed += (_, _) => _titleLookup.Dispose();
        Shown += async (_, _) => await ResolveTitlesAsync();
        Render();
    }

    private void DrawCell(Graphics graphics, string text, Rectangle bounds, Color color)
    {
        bounds.Inflate(-10, 0);
        TextRenderer.DrawText(graphics, text, Font, bounds, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void AddAction(string text, int x, int width, Action action)
    {
        var button = MakeButton(L.T(text), new Point(x, 454), new Size(width, 32));
        button.Click += (_, _) => RunAction(action);
        Controls.Add(button);
    }

    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
            System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ResolveTitlesAsync()
    {
        var ids = _mods.Select(mod => mod.WorkshopId).OfType<ulong>().ToArray();
        if (ids.Length == 0) return;
        try
        {
            var titles = await WorkshopTitles.ResolveAsync(ids, _titleLookup.Token);
            if (IsDisposed) return;
            for (int index = 0; index < _mods.Count; index++)
            {
                var mod = _mods[index];
                if (mod.WorkshopId is ulong id && titles.TryGetValue(id, out string? title))
                {
                    _mods[index] = mod with { Name = title };
                    _list.Items[index].Text = title;
                }
            }
            ModLibrary.Save(_mods, _gamePath);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or
            UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Stored names and Workshop IDs are still usable offline.
        }
    }

    private void CopyList()
    {
        var mods = _mods.Where(mod => mod.Enabled)
            .Select(mod => new SharedMod(mod.WorkshopId, mod.Name)).ToArray();
        var profile = new SharedModList(1, mods);
        Clipboard.SetText(JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
        _status.Text = L.T("Active mod list copied.");
    }

    private void PasteList()
    {
        string text = Clipboard.GetText();
        if (text.Length is 0 or > 131072)
            throw new InvalidDataException(L.T("Copy a Force Bond mod list first."));
        var profile = JsonSerializer.Deserialize<SharedModList>(text);
        if (profile?.Version != 1 || profile.Mods is not { Length: <= 500 } ||
            profile.Mods.Any(mod => mod is null || mod.WorkshopId == 0 || mod.Name is not { Length: > 0 and <= 512 }))
            throw new InvalidDataException(L.T("Copy a Force Bond mod list first."));

        var installed = ModLibrary.Load(_gamePath);
        var missingWorkshop = new List<ulong>();
        var matched = new List<ManagedMod>();
        var missing = new List<string>();
        foreach (var entry in profile.Mods)
        {
            var candidates = installed.Where(mod => ModLibrary.IsPackage(mod.Path) &&
                (entry.WorkshopId is ulong id ? mod.WorkshopId == id :
                    mod.Source == "Nexus Mods" && string.Equals(mod.Name, entry.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (candidates.Length != 1)
            {
                if (entry.WorkshopId is ulong missingId && candidates.Length == 0) missingWorkshop.Add(missingId);
                missing.Add(entry.WorkshopId is ulong workshop ? $"{entry.Name} ({workshop})" : entry.Name);
                continue;
            }
            if (matched.Any(mod => mod.Path.Equals(candidates[0].Path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(L.T("Copy a Force Bond mod list first."));
            matched.Add(candidates[0] with { Enabled = true });
        }
        if (missing.Count > 0)
        {
            string items = string.Join(Environment.NewLine, missing.Take(12));
            if (missing.Count > 12) items += $"{Environment.NewLine}+{missing.Count - 12}";
            if (missingWorkshop.Count == 1)
            {
                if (MessageBox.Show(this,
                "Open the missing mod in Steam Workshop? Subscribe, wait for Steam to download it, then paste the list again." +
                Environment.NewLine + Environment.NewLine + items, Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(WorkshopTitles.Page(missingWorkshop[0])) { UseShellExecute = true })?.Dispose();
                return;
            }
            MessageBox.Show(this, L.T("Install or register the missing mods, then paste the list again.") +
                Environment.NewLine + Environment.NewLine + items, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, L.T("Replace your active mods with this list?"), Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var included = matched.Select(mod => mod.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Commit([.. matched, .. installed.Where(mod => !included.Contains(mod.Path)).Select(mod => mod with { Enabled = false })]);
    }

    private void AddPackage()
    {
        using var dialog = new FolderBrowserDialog { Description = L.T("Select a separate mod folder containing Override, Modules or Lips.") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dialog.SelectedPath));
        if (!ModLibrary.IsPackage(path) || string.Equals(path, Path.TrimEndingDirectorySeparator(_gamePath), StringComparison.OrdinalIgnoreCase)
            || _mods.Any(mod => string.Equals(mod.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, L.T("Select a separate mod folder containing Override, Modules or Lips."), Text);
            return;
        }
        Commit([.. _mods, new(path, Path.GetFileName(path), "Nexus Mods", false)]);
    }

    private void Commit(List<ManagedMod> next)
    {
        try
        {
            ModLibrary.Save(next, _gamePath);
            _mods.Clear();
            _mods.AddRange(next);
            Render();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Render()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        for (int index = 0; index < _mods.Count; index++)
        {
            var mod = _mods[index];
            var row = new ListViewItem(mod.Name) { Tag = index, ToolTipText = mod.Path, UseItemStyleForSubItems = false };
            row.SubItems.Add(mod.Source);
            var status = row.SubItems.Add(L.T(mod.Enabled ? "Active" : "Inactive"));
            status.ForeColor = mod.Enabled ? Color.LightGreen : Color.LightCoral;
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        _status.Text = $"{L.T("Active")}: {_mods.Count(mod => mod.Enabled)} / {_mods.Count}";
    }
}
