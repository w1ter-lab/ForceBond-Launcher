using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ForceBond.Updates;

namespace ForceBond.Launcher;
internal static class LauncherUpdates
{
    public static UpdateSource Source { get; } = LoadSource();
    private static UpdateSource LoadSource()
    {
        using var stream = typeof(LauncherUpdates).Assembly.GetManifestResourceStream("ForceBond.UpdateSource")!;
        return JsonSerializer.Deserialize<UpdateSource>(stream, UpdateFiles.Json)!;
    }
    public static bool Redirect(string[] args)
    {
        if (!Source.Enabled || args.Any(a => a.StartsWith("--preview", StringComparison.Ordinal) || a.StartsWith("--update-ack=", StringComparison.Ordinal) || a == "--ignore-installed-update")) return false;
        try
        {
            if (!UpdateFiles.RecoverInterrupted(UpdateFiles.Root)) return false;
            string? current = UpdateFiles.ResolveCurrent(UpdateFiles.Root, Source.PublicKeyPem);
            if (current is null || Path.GetFullPath(current).Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
            // Never redirect a manually launched newer build to an older installed one.
            var info = FileVersionInfo.GetVersionInfo(current);
            if (Version.TryParse(info.FileVersion, out var installed) && installed < typeof(LauncherUpdates).Assembly.GetName().Version) return false;
            var start = new ProcessStartInfo(current) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(current)! };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) { Log(ex.Message); return false; }
    }
    public static void Acknowledge(string[] args)
    {
        string? arg = args.FirstOrDefault(a => a.StartsWith("--update-ack=", StringComparison.Ordinal));
        if (arg is null) return;
        string token = arg[13..];
        if (!System.Text.RegularExpressions.Regex.IsMatch(token, "^[0-9a-f]{32}$")) return;
        Directory.CreateDirectory(UpdateFiles.Root);
        File.WriteAllText(Path.Combine(UpdateFiles.Root, "ack-" + token), "ready");
    }
    public static HttpClient Client()
    {
        var client = BetaAccess.Client();
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ForceBond-Updater/1.0");
        return client;
    }
    public static async Task<byte[]> GetLimited(HttpClient client, string url, long limit, CancellationToken token, Action<long,long?>? progress = null)
    {
        using var response = await BetaAccess.SendAsync(client, url, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("Update download left HTTPS.");
        long? length = response.Content.Headers.ContentLength;
        if (length > limit) throw new IOException("Update download exceeds the size limit.");
        using var output = new MemoryStream();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[81920]; int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + read > limit) throw new IOException("Update download exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), token); progress?.Invoke(output.Length, length);
        }
        return output.ToArray();
    }
    public static async Task CheckAsync(Form owner, CancellationToken cancellation)
    {
        if (!Source.Enabled || !BetaAccess.Configured) return;
        try
        {
            using var client = Client();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromSeconds(7));
            await BetaAccess.Configuration(deadline.Token);
            if (!BetaAccess.PublicMode && BetaAccess.Session() is null) { Log("DISCORD_SIGN_IN_REQUIRED_FOR_UPDATES"); return; }
            byte[] bytes = await GetLimited(client, BetaAccess.ManifestUrl, 4 * 1024 * 1024, deadline.Token);
            var signed = JsonSerializer.Deserialize<SignedRelease>(bytes, UpdateFiles.Json) ?? throw new IOException("Empty update response.");
            var manifest = UpdateFiles.Verify(signed, Source.PublicKeyPem);
            var current = typeof(LauncherUpdates).Assembly.GetName().Version!;
            if (UpdateFiles.VersionOf(manifest.Version) <= current) return;
            if (owner.IsDisposed || cancellation.IsCancellationRequested) return;
            using var dialog = new UpdateWindow(manifest, signed);
            dialog.ShowDialog(owner);
            if (dialog.InstallStarted) owner.Close();
        }
        catch (OperationCanceledException) { Log("Update check cancelled or timed out; continuing offline."); }
        catch (Exception ex) { Log(ex.Message); }
    }
    public static void Log(string message)
    {
        try { Directory.CreateDirectory(UpdateFiles.Root); File.AppendAllText(Path.Combine(UpdateFiles.Root, "updater.log"), $"{DateTime.UtcNow:O} {message}\n"); } catch { }
    }
}
internal sealed class UpdateWindow : UtilityWindow
{
    public bool InstallStarted { get; private set; }
    public UpdateWindow(ReleaseManifest manifest, SignedRelease signed) : base("Force Bond update", new Size(600, 390))
    {
        Controls.Add(new Label { Text = "Force Bond " + manifest.Version, Location = new Point(24, 76), Size = new Size(552, 30), ForeColor = Teal, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        Controls.Add(new TextBox { Text = manifest.Notes, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(24, 118), Size = new Size(552, 144), BackColor = BackColor, ForeColor = Ink, BorderStyle = BorderStyle.None });
        var status = new Label { Text = L.T("Close KOTOR II before installing. The previous build is kept."), Location = new Point(24, 278), Size = new Size(552, 38) };
        Controls.Add(status);
        var later = MakeButton("LATER", new Point(24, 334), new Size(140, 36)); Controls.Add(later);
        var install = MakeButton("Update", new Point(326, 334), new Size(250, 36)); Controls.Add(install);
        bool busy = false;
        CancellationTokenSource? download = null;
        later.Click += (_, _) => { if (busy) download?.Cancel(); else Close(); };
        FormClosing += (_, e) => e.Cancel = busy;
        install.Click += async (_, _) =>
        {
            if (busy) return;
            var games = Process.GetProcessesByName("swkotor2"); bool running = games.Length > 0; foreach (var game in games) game.Dispose();
            if (running) { status.Text = L.T("Close KOTOR II before installing updates."); return; }
            busy = true; install.Enabled = false; later.Text = L.T("CANCEL");
            try
            {
                string directory = Path.Combine(UpdateFiles.Root, "downloads", Guid.NewGuid().ToString("N"));
                UpdateFiles.NoLinks(directory); Directory.CreateDirectory(directory);
                using var client = LauncherUpdates.Client(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                download = timeout;
                string zip = Path.Combine(directory, "package.zip");
                // Stream to disk; packages must never be buffered in x86 process memory.
                using (var response = await BetaAccess.SendAsync(client, manifest.PackageUrl, timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("Update download left HTTPS.");
                    if (response.Content.Headers.ContentLength is long length && length != manifest.PackageSize) throw new IOException("Package size does not match.");
                    await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                    await using var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    byte[] buffer = new byte[81920]; long received = 0; int count;
                    while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                    {
                        received += count;
                        if (received > manifest.PackageSize) throw new IOException("Update package exceeds its signed size.");
                        await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                        status.Text = L.T("Downloading update...") + $" {received * 100 / manifest.PackageSize}%";
                    }
                }
                if (new FileInfo(zip).Length != manifest.PackageSize || !UpdateFiles.SameHash(await Task.Run(() => UpdateFiles.Hash(zip)), manifest.PackageSha256)) throw new IOException("Downloaded package is damaged.");
                timeout.Token.ThrowIfCancellationRequested();
                string request = Path.Combine(directory, "request.json"); UpdateFiles.WriteAtomic(request, signed);
                string helper = Path.Combine(AppContext.BaseDirectory, "ForceBond.Updater.exe");
                if (!File.Exists(helper)) throw new FileNotFoundException("Update helper is missing. Extract the complete Force Bond package.");
                var start = new ProcessStartInfo(helper) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
                start.ArgumentList.Add(Environment.ProcessId.ToString()); start.ArgumentList.Add(request); start.ArgumentList.Add(Environment.ProcessPath!);
                using var helperProcess = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
                InstallStarted = true; busy = false; Close();
            }
            catch (OperationCanceledException) { status.Text = L.T("Download cancelled. Your current build is unchanged."); }
            catch (Exception ex) { status.Text = L.T("Update failed. Your current build is unchanged."); LauncherUpdates.Log(ex.ToString()); MessageBox.Show(this, ex.Message, L.T("Force Bond update")); }
            finally { download = null; busy = false; install.Enabled = later.Enabled = true; later.Text = L.T("LATER"); }
        };
        L.Apply(this);
    }
}
