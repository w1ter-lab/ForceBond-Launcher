using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ForceBond.Updates;

namespace ForceBond.Launcher;

internal static class NativeMigration
{
    internal static string Run(string gamePath, string? dataRoot = null)
    {
        string game = Path.GetFullPath(gamePath), data = Path.GetFullPath(dataRoot ?? LauncherSettings.DataDirectory);
        if (ProcessRunning("swkotor2") || ProcessRunning("OdysseyMP")) throw new IOException("Close the game before migrating old mod files.");
        UpdateFiles.NoLinks(game); UpdateFiles.NoLinks(data);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ForceBond.LegacyFiles")!;
        var known = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
        string archive = Path.Combine(game, "ForceBond-backups", "native-migration-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        var moves = new List<(string Source, string Relative)>();
        var preserved = new List<string>();
        foreach (var entry in known)
        {
            string file = Path.GetFullPath(Path.Combine(game, entry.Key));
            if (!file.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid migration path.");
            if (!File.Exists(file)) continue;
            UpdateFiles.NoLinks(file);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Migration cannot move symbolic links.");
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            if (entry.Value.Any(h => h.Equals(hash, StringComparison.OrdinalIgnoreCase))) moves.Add((file, "game/" + entry.Key));
            else preserved.Add(entry.Key); // altered or third-party files stay untouched
        }
        foreach (string name in new[] { "chat_input_state.txt", "chat_to_game.txt", "menu_command.txt", "network_active.flag", "network_status.txt", "voice_hud.txt" })
        {
            string file = Path.Combine(data, name);
            if (File.Exists(file)) { UpdateFiles.NoLinks(file);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Migration cannot move symbolic links."); moves.Add((file, "legacy-state/" + name)); }
        }
        // This is a reversible migration out of the live install, not an Override wipe.
        foreach (var move in moves)
        {
            string target = Path.Combine(archive, move.Relative);
            UpdateFiles.NoLinks(Path.GetDirectoryName(target)!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(move.Source, target);
        }
        if (moves.Count > 0 || preserved.Count > 0)
        {
            Directory.CreateDirectory(archive);
            File.WriteAllText(Path.Combine(archive, "migration.json"), JsonSerializer.Serialize(new { moved = moves.Select(m => new { source=m.Source, archived=m.Relative }), preserved }, new JsonSerializerOptions { WriteIndented=true }));
        }
        CacheMaintenance.Clean(data, new[] { Environment.ProcessPath! }, pid => { try { using var p=System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; } catch { return false; } });
        return $"Native migration: {moves.Count} obsolete files archived; {preserved.Count} modified files preserved.";
    }
    private static bool ProcessRunning(string name) { var all=System.Diagnostics.Process.GetProcessesByName(name); bool any=all.Length>0; foreach(var p in all)p.Dispose(); return any; }
}
