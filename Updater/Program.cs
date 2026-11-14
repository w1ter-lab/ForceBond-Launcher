using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ForceBond.Updates;

namespace ForceBond.Updater;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallWindow(args));
    }
}
internal sealed class InstallWindow : Form
{
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(24), ForeColor = Color.Turquoise };
    private bool busy = true;
    public InstallWindow(string[] args)
    {
        Text = "Force Bond — Update"; ClientSize = new Size(510, 135); BackColor = Color.FromArgb(7, 16, 18);
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
        Font = new Font("Segoe UI", 10); Controls.Add(status);
        FormClosing += (_, e) => e.Cancel = busy;
        Shown += async (_, _) =>
        {
            try { await Install(args); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(UpdateFiles.Root);
                File.AppendAllText(Path.Combine(UpdateFiles.Root, "updater.log"), $"{DateTime.UtcNow:O} {ex}\n");
                MessageBox.Show(this, "Update was not activated. Your previous build is still available.\n\n" + ex.Message, "Force Bond", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { busy = false; Close(); }
        };
    }
    private async Task Install(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[0], out int parentPid)) throw new InvalidDataException("Invalid updater arguments.");
        string requestPath = Path.GetFullPath(args[1]), fallback = Path.GetFullPath(args[2]);
        string root = UpdateFiles.Root; UpdateFiles.NoLinks(root);
        if (!requestPath.StartsWith(Path.Combine(root, "downloads") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fallback) || !Path.GetFileName(fallback).Equals("ForceBond.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid update request path.");
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ForceBond.UpdateSource")!;
        var source = JsonSerializer.Deserialize<UpdateSource>(stream, UpdateFiles.Json)!;
        if (!source.Enabled) throw new InvalidOperationException("Update publishing is not configured.");
        var signed = UpdateFiles.Read<SignedRelease>(requestPath);
        var manifest = UpdateFiles.Verify(signed, source.PublicKeyPem);
        string zip = Path.Combine(Path.GetDirectoryName(requestPath)!, "package.zip");
        using var updateLock = new FileStream(Path.Combine(root, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!UpdateFiles.RecoverInterrupted(root)) throw new IOException("Another update is active.");
        status.Text = "Waiting for the launcher to close…";
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await parent.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException) { }
        EnsureGameClosed();
        string pointer = Path.Combine(root, "current.json");
        if (File.Exists(pointer))
        {
            var installed = UpdateFiles.Verify(UpdateFiles.Read<CurrentRelease>(pointer).Release, source.PublicKeyPem);
            if (UpdateFiles.VersionOf(installed.Version) >= UpdateFiles.VersionOf(manifest.Version)) throw new IOException("This version is already installed or an older update was offered.");
        }
        string folder = "version-" + Guid.NewGuid().ToString("N"), destination = Path.Combine(root, folder);
        status.Text = "Checking and installing the complete Force Bond package…";
        await Task.Run(() => UpdateFiles.ExtractVerified(zip, destination, manifest));
        EnsureGameClosed();
        using var self = Process.GetCurrentProcess();
        UpdateFiles.BeginActivation(root, new CurrentRelease(folder, signed), Environment.ProcessId, self.StartTime.ToUniversalTime().Ticks);
        string token = Guid.NewGuid().ToString("N"), ack = Path.Combine(root, "ack-" + token);
        try
        {
            status.Text = "Starting the updated launcher…";
            var start = new ProcessStartInfo(Path.Combine(destination, "ForceBond.exe")) { UseShellExecute = false, WorkingDirectory = destination };
            start.ArgumentList.Add("--update-ack=" + token);
            using var child = Process.Start(start) ?? throw new IOException("Updated launcher did not start.");
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (!File.Exists(ack) && !child.HasExited && DateTime.UtcNow < deadline) await Task.Delay(200);
            if (!File.Exists(ack)) throw new IOException("Updated launcher did not confirm startup. The previous version was restored.");
            UpdateFiles.FinishActivation(root);
            CacheMaintenance.RemoveCompletedDownload(requestPath);
            try {if(File.Exists(ack))File.Delete(ack);} catch(IOException) {} catch(UnauthorizedAccessException) {}
            updateLock.Dispose();
            CacheMaintenance.Run();
            File.AppendAllText(Path.Combine(root, "updater.log"), $"{DateTime.UtcNow:O} ACTIVATED {manifest.Version} {folder}\n");
        }
        catch
        {
            if (File.Exists(Path.Combine(root, "pending.json"))) UpdateFiles.RollbackActivation(root);
            var restore = new ProcessStartInfo(fallback) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(fallback)! };
            restore.ArgumentList.Add("--ignore-installed-update");
            Process.Start(restore)?.Dispose();
            throw;
        }
    }
    private static void EnsureGameClosed()
    {
        var games = Process.GetProcessesByName("swkotor2").Concat(Process.GetProcessesByName("OdysseyMP")).ToArray();
        bool running = games.Length != 0;
        foreach (var game in games) game.Dispose();
        if (running) throw new IOException("Close KOTOR II before installing updates.");
    }
}
