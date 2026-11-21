using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ForceBond.Launcher;

internal static class OdysseyEngine
{
    internal static string DirectoryPath => File.Exists(Path.Combine(AppContext.BaseDirectory, "Engine", "OdysseyMP.exe")) ? Path.Combine(AppContext.BaseDirectory, "Engine") : Path.Combine(AppContext.BaseDirectory, "Support", "OdysseyMP");
    internal static string Executable => Path.Combine(DirectoryPath, "OdysseyMP.exe");
    internal static bool Enabled => true; // native launcher: never silently fall back to the injected runtime
    private const ulong RussianWorkshopId = 589057016;
    internal static bool Available => File.Exists(Executable);

    internal static string Verify(string gamePath)
    {
        foreach (string file in new[] { "OdysseyMP.exe", "steam_api64.dll", "shaderpack.erf", "SDL3.dll", "OpenAL32.dll", "GameNetworkingSockets.dll" })
            if (!File.Exists(Path.Combine(DirectoryPath, file))) throw new FileNotFoundException("OdysseyMP package is incomplete: " + file);
        if (!File.Exists(Path.Combine(gamePath, "chitin.key")) || !File.Exists(Path.Combine(gamePath, "dialog.tlk")))
            throw new InvalidDataException("Select the original KOTOR II installation containing chitin.key and dialog.tlk.");
        // Old injected per-area modules must not override the original module.
        if (File.Exists(Path.Combine(gamePath, "Modules", "101PER.mod")))
            throw new InvalidDataException("Modules/101PER.mod overrides the original game. Restore the original module before using OdysseyMP; no files were changed.");
        return "OdysseyMP package found. Original game files are read without installing the old DLL or Override scripts.";
    }

    internal static Process Start(string gamePath, LauncherSettings settings, bool headlessCheck = false)
    {
        Verify(gamePath);
        string profile = Path.Combine(LauncherSettings.DataDirectory, "OdysseyMP", headlessCheck ? "launch-check" : settings.EngineMode);
        Directory.CreateDirectory(profile);
        File.Copy(Path.Combine(DirectoryPath, "shaderpack.erf"), Path.Combine(profile, "shaderpack.erf"), true);
        var start = new ProcessStartInfo(Executable) { WorkingDirectory = profile, UseShellExecute = false, CreateNoWindow = true, WindowStyle = headlessCheck ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal };
        foreach (string arg in new[] { "--game", gamePath, "--steam", "1", "--coop-mode", settings.EngineMode,
            "--hud", "k2", "--forcebond-settings", LauncherSettings.FilePath, "--forcebond-debug-menu", settings.DebugMenuEnabled ? "1" : "0", "--run-unfocused", "1" }) start.ArgumentList.Add(arg);
        start.Environment["FORCEBOND_ROLEPLAY_MENU"] = !headlessCheck && settings.Mode == "roleplay" ? "1" : "0";
        start.Environment.Remove("FORCEBOND_RUSSIAN_LOCALIZATION");
        start.Environment["FORCEBOND_GAME_LANGUAGE"] = settings.GameLanguage == "ru" ? "ru" : "en";
        start.Environment["FORCEBOND_LOCALIZATION_ROOT"] = Path.Combine(DirectoryPath, "Localization", "ru");
        if (!headlessCheck)
        {
            var mods = ModLibrary.Load(gamePath);
            foreach (var mod in mods.Where(mod => mod.WorkshopId != RussianWorkshopId && mod.Enabled && ModLibrary.IsPackage(mod.Path)))
            {
                start.ArgumentList.Add("--forcebond-mod-root");
                start.ArgumentList.Add(mod.Path);
            }
        }
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
        if (headlessCheck) {
            foreach (var arg in new[] { "--headless", "1", "--control-port", "47508", "--width", "960", "--height", "540", "--fullscreen", "0", "--borderless", "0" }) start.ArgumentList.Add(arg);
        } else if (bounds is not null) {
            foreach (var arg in new[] { "--width", bounds.Value.Width.ToString(), "--height", bounds.Value.Height.ToString(),
                "--fullscreen", settings.BorderlessFullscreen ? "0" : "1", "--borderless", settings.BorderlessFullscreen ? "1" : "0" }) start.ArgumentList.Add(arg);
        }
        return Process.Start(start) ?? throw new IOException("OdysseyMP could not be started.");
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    internal static Process? FindRunning()
    {
        foreach (var p in Process.GetProcessesByName("OdysseyMP"))
        {
            IntPtr handle = OpenProcess(0x1000, false, p.Id);
            try {
                int length = 32768; var path = new StringBuilder(length);
                if (handle != IntPtr.Zero && QueryFullProcessImageName(handle, 0, path, ref length) &&
                    string.Equals(Path.GetFullPath(path.ToString()), Path.GetFullPath(Executable), StringComparison.OrdinalIgnoreCase)) return p;
            } finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
            p.Dispose();
        }
        return null;
    }
}
