using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ForceBond.Updates;

internal sealed record UpdateSource(string ManifestUrl, string PublicKeyPem)
{
    public bool Enabled => !string.IsNullOrWhiteSpace(ManifestUrl) && !string.IsNullOrWhiteSpace(PublicKeyPem);
}
internal sealed record PackageFile(string Path, long Size, string Sha256);
internal sealed record ReleaseManifest(int Schema, string Version, string Notes, string PackageUrl, long PackageSize, string PackageSha256, PackageFile[] Files);
internal sealed record SignedRelease(string Payload, string Signature);
internal sealed record CurrentRelease(string Folder, SignedRelease Release);
internal sealed record PendingActivation(string? Previous, int InstallerPid, long InstallerStart);
internal static class UpdateFiles
{
    public const long MaxPackage = 768L * 1024 * 1024;
    public const long MaxExpanded = 1536L * 1024 * 1024;
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOTOR2_Coop", "Updates");
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Invalid update data.");
    public static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(value, Json));
        File.Move(temp, path, true);
    }
    public static Uri Https(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)
            throw new InvalidDataException("Updates require an HTTPS address.");
        return uri;
    }
    public static Version VersionOf(string version)
    {
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(\.\d+)?$")) throw new InvalidDataException("Invalid update version.");
        return Version.Parse(version);
    }
    public static string SafeRelative(string name)
    {
        string normalized = name.Replace('\\', '/');
        var parts = normalized.Split('/');
        if (normalized.Length > 220 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.Any(c => c < 32 || ":*?\"<>|".Contains(c)) || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Unsafe update path: " + name);
        // Packages own only the launcher, its icon, readme and the existing Support payload.
        if (!(normalized.Equals("ForceBond.exe", StringComparison.OrdinalIgnoreCase) || normalized.Equals("ForceBond.Updater.exe", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("logo.ico", StringComparison.OrdinalIgnoreCase) || normalized.Equals("README.txt", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Support/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("Engine/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("EngineSource.zip", StringComparison.OrdinalIgnoreCase) || normalized.Equals("package-manifest.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unexpected update file: " + name);
        return normalized;
    }
    public static void NoLinks(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir is not null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Updates cannot use redirected directories.");
    }
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static bool SameHash(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    public static ReleaseManifest Verify(SignedRelease signed, string publicKey)
    {
        byte[] data = Convert.FromBase64String(signed.Payload), signature = Convert.FromBase64String(signed.Signature);
        if (data.Length > 2 * 1024 * 1024) throw new InvalidDataException("Update manifest too large.");
        using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey);
        if (rsa.KeySize < 2048 || !rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("Update signature is not valid.");
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(data, Json) ?? throw new InvalidDataException("Missing update manifest.");
        Validate(manifest);
        return manifest;
    }
    public static void Validate(ReleaseManifest m)
    {
        if (m.Schema != 1 || m.Files is null || m.Files.Length is < 3 or > 10000 || m.PackageSize is <= 0 or > MaxPackage ||
            m.Notes is null || m.Notes.Length > 12000 || !Regex.IsMatch(m.PackageSha256 ?? "", "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Invalid update manifest.");
        VersionOf(m.Version); Https(m.PackageUrl);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long size = 0;
        foreach (var f in m.Files)
        {
            if (!unique.Add(SafeRelative(f.Path)) || f.Size < 0 || f.Size > MaxExpanded || !Regex.IsMatch(f.Sha256 ?? "", "^[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("Invalid update file list.");
            size = checked(size + f.Size);
        }
        bool native = unique.Contains("Engine/OdysseyMP.exe") || unique.Contains("Support/OdysseyMP/OdysseyMP.exe");
        string engine = unique.Contains("Engine/OdysseyMP.exe") ? "Engine/" : "Support/OdysseyMP/";
        bool payload = native
            ? new[] { "OdysseyMP.exe", "steam_api64.dll", "SDL3.dll", "OpenAL32.dll", "GameNetworkingSockets.dll", "shaderpack.erf" }.All(f => unique.Contains(engine + f))
            : unique.Contains("Support/OdysseyMPCoop.dll") && unique.Contains("Support/OverrideAssets/globalcat.2da");
        if (size > MaxExpanded || !unique.Contains("ForceBond.exe") || !unique.Contains("ForceBond.Updater.exe") || !payload)
            throw new InvalidDataException("Incomplete Force Bond package.");
    }
    public static void ExtractVerified(string zip, string destination, ReleaseManifest m)
    {
        if (new FileInfo(zip).Length != m.PackageSize || !SameHash(Hash(zip), m.PackageSha256)) throw new InvalidDataException("Downloaded package is damaged.");
        NoLinks(destination);
        if (Directory.Exists(destination)) throw new IOException("Update destination already exists.");
        using var archive = ZipFile.OpenRead(zip);
        var expected = m.Files.ToDictionary(f => SafeRelative(f.Path), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Validate the entire directory before writing any executable.
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) { SafeRelative(entry.FullName.TrimEnd('/') + "/placeholder"); continue; }
            string name = SafeRelative(entry.FullName);
            if (!seen.Add(name) || !expected.TryGetValue(name, out var f) || f.Size != entry.Length || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Archive does not match its signed file list.");
        }
        if (seen.Count != expected.Count) throw new InvalidDataException("Update archive is incomplete.");
        Directory.CreateDirectory(destination);
        foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            string name = SafeRelative(entry.FullName), target = Path.Combine(destination, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = entry.Open())
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920]; long written = 0; int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    written += count;
                    if (written > expected[name].Size) throw new InvalidDataException("Expanded file exceeds signed size.");
                    output.Write(buffer, 0, count);
                }
            }
            if (new FileInfo(target).Length != expected[name].Size || !SameHash(Hash(target), expected[name].Sha256))
                throw new InvalidDataException("An update file is damaged: " + name);
        }
    }
    public static string? ResolveCurrent(string root, string publicKey)
    {
        NoLinks(root); string pointer = Path.Combine(root, "current.json");
        if (!File.Exists(pointer)) return null;
        var current = Read<CurrentRelease>(pointer);
        if (!Regex.IsMatch(current.Folder, @"^version-[0-9a-f]{32}$")) throw new InvalidDataException("Invalid installed update path.");
        var manifest = Verify(current.Release, publicKey);
        string folder = Path.Combine(root, current.Folder); NoLinks(folder);
        foreach (var file in manifest.Files)
        {
            string path = Path.Combine(folder, SafeRelative(file.Path).Replace('/', Path.DirectorySeparatorChar));
            NoLinks(Path.GetDirectoryName(path)!);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != file.Size || !SameHash(Hash(path), file.Sha256))
                throw new InvalidDataException("Installed update verification failed: " + file.Path);
        }
        return Path.Combine(folder, "ForceBond.exe");
    }
    public static void BeginActivation(string root, CurrentRelease next, int pid, long start)
    {
        string pointer = Path.Combine(root, "current.json"), pending = Path.Combine(root, "pending.json");
        if (File.Exists(pending)) throw new IOException("An earlier update requires recovery.");
        string? previous = File.Exists(pointer) ? File.ReadAllText(pointer) : null;
        if (previous is not null) File.WriteAllText(Path.Combine(root, "previous.json"), previous);
        WriteAtomic(pending, new PendingActivation(previous, pid, start));
        WriteAtomic(pointer, next);
    }
    public static void FinishActivation(string root)
    {
        File.Move(Path.Combine(root, "pending.json"), Path.Combine(root, "activation-" + Guid.NewGuid().ToString("N") + ".json"));
    }
    public static void RollbackActivation(string root)
    {
        string pending = Path.Combine(root, "pending.json"), pointer = Path.Combine(root, "current.json");
        var state = Read<PendingActivation>(pending);
        if (state.Previous is null)
        {
            if (File.Exists(pointer)) File.Move(pointer, Path.Combine(root, "failed-" + Guid.NewGuid().ToString("N") + ".json"));
        }
        else
        {
            // Parse before restoring; recovery never writes outside the update cache.
            var previous = JsonSerializer.Deserialize<CurrentRelease>(state.Previous, Json) ?? throw new InvalidDataException("Invalid rollback journal.");
            WriteAtomic(pointer, previous);
        }
        File.Move(pending, Path.Combine(root, "rollback-" + Guid.NewGuid().ToString("N") + ".json"));
    }
    public static bool RecoverInterrupted(string root)
    {
        NoLinks(root);
        string pending = Path.Combine(root, "pending.json");
        if (!File.Exists(pending)) return true;
        var state = Read<PendingActivation>(pending);
        try
        {
            using var installer = System.Diagnostics.Process.GetProcessById(state.InstallerPid);
            if (!installer.HasExited && installer.StartTime.ToUniversalTime().Ticks == state.InstallerStart) return false;
        }
        catch (ArgumentException) { }
        RollbackActivation(root);
        return true;
    }
}
