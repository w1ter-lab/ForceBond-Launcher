using Microsoft.Win32;

namespace ForceBond.Launcher;

internal static class DiscordLaunch
{
    internal const string Protocol = "discord-1558885772889690193";
    internal const string Argument = "--discord-launch";
    internal static bool Requested(IEnumerable<string> args) => args.Contains(Argument, StringComparer.Ordinal);

    internal static string OpenCommand(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Invalid launcher path.", nameof(executable));
        return "\"" + Path.GetFullPath(executable) + "\" " + Argument;
    }

    // Same per-user launch registration used by Discord's RPC library. Discord
    // delivers ACTIVITY_JOIN when the launched game's IPC connection is ready.
    // No arbitrary URL/command is accepted from a protocol argument.
    internal static void Register(string executable)
    {
        string command = OpenCommand(executable);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Protocol);
        key.SetValue("", "URL:Force Bond Discord invitation");
        key.SetValue("URL Protocol", "");
        using var icon = key.CreateSubKey("DefaultIcon");
        icon.SetValue("", "\"" + Path.GetFullPath(executable) + "\",0");
        using var open = key.CreateSubKey(@"shell\open\command");
        open.SetValue("", command);
    }
}
