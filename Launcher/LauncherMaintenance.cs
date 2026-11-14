using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ForceBond.Launcher;
internal static class VerifiedGameResources
{
    private sealed record Receipt(string Version, string GamePath);
    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static string ReceiptPath(string dataRoot, string gamePath) => Path.Combine(dataRoot, "verified-game-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(gamePath).ToUpperInvariant())))) + ".json";
    public static bool NeedsVerification(string dataRoot, string version, string gamePath)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(ReceiptPath(dataRoot, gamePath)));
            return receipt is null || receipt.Version != version || !string.Equals(receipt.GamePath, Normalize(gamePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return true; }
    }
    public static void Record(string dataRoot, string version, string gamePath)
    {
        Directory.CreateDirectory(dataRoot);
        string path = ReceiptPath(dataRoot, gamePath), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Receipt(version, Normalize(gamePath))));
        File.Move(temp, path, true);
    }
}
internal static class LauncherWindowPlacement
{
    public static Point? Load(string path)
    {
        try { var position = JsonSerializer.Deserialize<Position>(File.ReadAllText(path)); return position is null ? null : new Point(position.X, position.Y); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    private sealed record Position(int X, int Y);
    private static bool HeaderVisible(Point point, Rectangle area)
    {
        long x = (long)point.X + 80, y = (long)point.Y + 20;
        return x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom;
    }
    public static void Save(string path, Point position)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Position(position.X, position.Y)));
    }
    public static Rectangle ChooseArea(Point? saved, Rectangle[] areas, Point cursor)
    {
        if (areas.Length == 0) return new Rectangle(0, 0, 1920, 1080);
        if (saved is Point old)
            foreach (var area in areas) if (HeaderVisible(old, area)) return area;
        foreach (var area in areas) if (area.Contains(cursor)) return area;
        return areas[0];
    }
    public static Point Clamp(Size size, Point point, Rectangle area) => new(
        Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
        Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
    public static Point Place(Size size, Point? saved, Rectangle[] areas, Point cursor)
    {
        var area = ChooseArea(saved, areas, cursor);
        bool usable = saved is Point old && HeaderVisible(old, area);
        var requested = usable ? saved!.Value : new Point(area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2);
        return Clamp(size, requested, area);
    }
}
