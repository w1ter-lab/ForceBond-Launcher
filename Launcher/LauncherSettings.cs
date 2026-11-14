using System.Globalization;
using System.Text;

namespace ForceBond.Launcher;

internal sealed record LauncherSettings(int ChatKey = 84, int VoiceKey = 86, int VoiceVolume = 100, bool DiscordEnabled = true, string Language = "en", int ChatBubbleRange = 30, bool DebugMenuEnabled = false, bool BorderlessFullscreen = false, string Mode = "story", string GameLanguage = "en")
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOTOR2_Coop");
    public static string FilePath => Path.Combine(DataDirectory, "forcebond_user.ini");
    public static readonly int[] ChatKeys = [84, 89, 85, 117, 121]; // T, Y, U, F6, F10
    public static readonly int[] VoiceKeys = [86, 66, 78, 67, 118]; // V, B, N, C, F7
    public static string KeyName(int key) => key >= 112 ? $"F{key - 111}" : ((char)key).ToString();

    public static LauncherSettings Parse(string text)
    {
        var values = IniValues.Parse(text);
        int chat = values.Int("ChatKey", 84), voice = values.Int("VoiceKey", 86);
        bool debug = values.Int("DebugMenuEnabled", 0) == 1;
        if (debug && voice == 118) voice = 86;
        return new LauncherSettings(ChatKeys.Contains(chat) ? chat : 84, VoiceKeys.Contains(voice) ? voice : 86,
            Math.Clamp(values.Int("VoiceVolume", 100), 0, 100), values.Int("DiscordEnabled", 1) != 0,
            NormalizeLanguage(values.GetValueOrDefault("Language", "en")), Math.Clamp(values.Int("ChatBubbleRange", 30), 0, 100), debug, values.Int("BorderlessFullscreen", 0) == 1, NormalizeMode(values.GetValueOrDefault("Mode", "story")),
            values.GetValueOrDefault("GameLanguage") == "ru" ? "ru" : "en");
    }

    public static string NormalizeMode(string? mode) => mode?.ToLowerInvariant() is "story+" or "free" ? "story+" : "story";
    public string EngineMode => NormalizeMode(Mode) == "story+" ? "free" : "story";
    public int PlayerCapacity => EngineMode == "free" ? 8 : 3;

    public static string NormalizeLanguage(string? language) => language?.ToLowerInvariant() is "uk" or "ru" ? language.ToLowerInvariant() : "en";

    public static LauncherSettings Load()
    {
        try { return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : new(); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        string text = ToIni();
        Directory.CreateDirectory(DataDirectory);
        string temporary = FilePath + ".new";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Move(temporary, FilePath, true);
    }

    public string ToIni()
    {
        if (DebugMenuEnabled && VoiceKey == 118) throw new InvalidDataException("F7 is reserved while Debug Menu is enabled.");
        if (!ChatKeys.Contains(ChatKey) || !VoiceKeys.Contains(VoiceKey) || ChatKey == VoiceKey || VoiceVolume is < 0 or > 100 || ChatBubbleRange is < 0 or > 100)
            throw new InvalidDataException("Choose valid, distinct chat and voice keys.");
        return $"[ForceBond]\r\nChatKey={ChatKey}\r\nVoiceKey={VoiceKey}\r\nVoiceVolume={VoiceVolume}\r\nDiscordEnabled={(DiscordEnabled ? 1 : 0)}\r\nLanguage={NormalizeLanguage(Language)}\r\nChatBubbleRange={ChatBubbleRange}\r\nDebugMenuEnabled={(DebugMenuEnabled ? 1 : 0)}\r\nBorderlessFullscreen={(BorderlessFullscreen ? 1 : 0)}\r\nMode={NormalizeMode(Mode)}\r\nGameLanguage={(GameLanguage == "ru" ? "ru" : "en")}\r\n";
    }
}

internal static class IniValues
{
    public static Dictionary<string, string> Parse(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n'))
        {
            int split = line.IndexOf('=');
            if (split <= 0 || line.TrimStart().StartsWith(';')) continue;
            values[line[..split].Trim()] = line[(split + 1)..].Trim();
        }
        return values;
    }

    public static int Int(this Dictionary<string, string> values, string key, int fallback = 0) =>
        values.TryGetValue(key, out string? text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
}
