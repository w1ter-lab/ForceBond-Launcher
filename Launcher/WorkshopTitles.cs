using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace ForceBond.Launcher;

internal static class WorkshopTitles
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    internal static string Page(ulong id) =>
        $"https://steamcommunity.com/sharedfiles/filedetails/?id={id.ToString(CultureInfo.InvariantCulture)}";

    internal static async Task<Dictionary<ulong, string>> ResolveAsync(IEnumerable<ulong> ids, CancellationToken cancellation)
    {
        var titles = new Dictionary<ulong, string>();
        foreach (var batch in ids.Distinct().Chunk(50))
        {
            var fields = new Dictionary<string, string> { ["itemcount"] = batch.Length.ToString(CultureInfo.InvariantCulture) };
            for (int index = 0; index < batch.Length; index++)
                fields[$"publishedfileids[{index}]"] = batch[index].ToString(CultureInfo.InvariantCulture);

            using var body = new FormUrlEncodedContent(fields);
            using var response = await Client.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body, cancellation);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            foreach (var item in json.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
            {
                if (item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String &&
                    item.TryGetProperty("publishedfileid", out var key) && key.ValueKind == JsonValueKind.String &&
                    ulong.TryParse(key.GetString(), out ulong id) && batch.Contains(id) &&
                    title.GetString() is { Length: > 0 and <= 512 } name)
                    titles[id] = name;
            }
        }
        return titles;
    }
}

internal sealed record SharedMod(ulong? WorkshopId, string Name);
internal sealed record SharedModList(int Version, SharedMod[] Mods);
