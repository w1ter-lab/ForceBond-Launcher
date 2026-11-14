using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ForceBond.Launcher;

internal static class GameDisplay
{
    private sealed record DisplayKey(string Section, string Key, string? Value);
    private static readonly (string Section, string Key)[] Keys =
    [ ("Display Options", "FullScreen"), ("Graphics Options", "FullScreen"),
      ("Graphics Options", "AllowWindowedMode"), ("Graphics Options", "Width"), ("Graphics Options", "Height") ];

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(string section, string key, string fallback, StringBuilder value, uint size, string file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrivateProfileString(string section, string key, string? value, string file);

    private static string? Read(string file, string section, string key)
    {
        var text = new StringBuilder(256);
        GetPrivateProfileString(section, key, "__FORCEBOND_MISSING__", text, 256, file);
        return text.ToString() is "__FORCEBOND_MISSING__" ? null : text.ToString();
    }

    private static void Write(string file, DisplayKey key)
    {
        if (!WritePrivateProfileString(key.Section, key.Key, key.Value, file))
            throw new IOException("Could not update KOTOR II display settings.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }

    public static void RequireWindowedForRunningGame(string gamePath, bool enabled)
    {
        if (enabled && Read(Path.Combine(gamePath, "swkotor2.ini"), "Graphics Options", "FullScreen") != "0")
            throw new InvalidOperationException("Close KOTOR II and launch it again to apply Borderless Fullscreen.");
    }

    // Called only with the game closed. Keep original managed values across
    // repeated launches; restore only those keys when the option is disabled.
    public static void Prepare(string gamePath, bool enabled)
    {
        string root = Path.GetFullPath(gamePath);
        string ini = Path.Combine(root, "swkotor2.ini");
        string backups = Path.Combine(root, "ForceBond-backups");
        string state = Path.Combine(backups, "display-mode-state.json");
        if (!enabled && !File.Exists(state)) return;
        if (!File.Exists(ini)) throw new FileNotFoundException("KOTOR II configuration file is missing.", ini);
        if ((File.GetAttributes(ini) & FileAttributes.ReparsePoint) != 0 ||
            (Directory.Exists(backups) && (File.GetAttributes(backups) & FileAttributes.ReparsePoint) != 0) ||
            (File.Exists(state) && (File.GetAttributes(state) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Linked display configuration paths are not supported.");

        var before = Keys.Select(k => new DisplayKey(k.Section, k.Key, Read(ini, k.Section, k.Key))).ToArray();
        DisplayKey[] original = before;
        if (File.Exists(state))
        {
            original = JsonSerializer.Deserialize<DisplayKey[]>(File.ReadAllText(state)) ?? throw new IOException("Invalid display backup.");
            if (original.Length != Keys.Length || original.Select(k => (k.Section, k.Key)).Distinct().Count() != Keys.Length ||
                original.Any(k => !Keys.Contains((k.Section, k.Key)))) throw new IOException("Invalid display backup keys.");
        }
        Rectangle bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Primary display was not found.");
        DisplayKey[] desired = enabled ?
        [new("Display Options", "FullScreen", "0"), new("Graphics Options", "FullScreen", "0"),
         new("Graphics Options", "AllowWindowedMode", "1"),
         new("Graphics Options", "Width", bounds.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)),
         new("Graphics Options", "Height", bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture))] : original;
        Directory.CreateDirectory(backups);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        bool changed = desired.Any(k => Read(ini, k.Section, k.Key) != k.Value);
        if (changed) File.Copy(ini, Path.Combine(backups, "Display-" + stamp + ".ini"));
        if (enabled && !File.Exists(state))
        {
            string temporary = state + ".new";
            File.WriteAllText(temporary, JsonSerializer.Serialize(original));
            File.Move(temporary, state, false);
        }
        try
        {
            if (changed) foreach (DisplayKey key in desired) Write(ini, key);
        }
        catch
        {
            foreach (DisplayKey key in before) WritePrivateProfileString(key.Section, key.Key, key.Value, ini);
            throw;
        }
        if (!enabled && File.Exists(state))
            File.Move(state, Path.Combine(backups, "display-mode-restored-" + stamp + ".json"));
    }
}
