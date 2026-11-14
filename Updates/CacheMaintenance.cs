using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ForceBond.Updates;
internal static class CacheMaintenance
{
    // Only recognized disposable names are eligible. User data is never swept.
    internal static long Clean(string data, string[] runningPaths, Func<int,bool> processAlive)
    {
        data=Path.GetFullPath(data); UpdateFiles.NoLinks(data);
        long freed=0;
        void Remove(string path)
        {
            try {
                string full=Path.GetFullPath(path);
                if(!full.StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
                UpdateFiles.NoLinks(full);
                if(Directory.Exists(full)) {
                    // Preflight the whole tree without traversing junctions.
                    var pending=new Stack<string>();pending.Push(full);long bytes=0;
                    while(pending.Count>0)foreach(string child in Directory.EnumerateFileSystemEntries(pending.Pop())) {
                        var attr=File.GetAttributes(child);
                        if((attr&FileAttributes.ReparsePoint)!=0)return;
                        if((attr&FileAttributes.Directory)!=0)pending.Push(child);else bytes+=new FileInfo(child).Length;
                    }
                    Directory.Delete(full,true);freed+=bytes;
                } else if(File.Exists(full)) {long bytes=new FileInfo(full).Length;File.Delete(full);freed+=bytes;}
            } catch(IOException) {} catch(UnauthorizedAccessException) {}
        }
        string updates=Path.Combine(data,"Updates");
        if(Directory.Exists(updates)) {
            UpdateFiles.NoLinks(updates);
            try {
                using var gate=new FileStream(Path.Combine(updates,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                // Never interfere with activation or rollback; malformed pointers fail closed.
                if(!File.Exists(Path.Combine(updates,"pending.json"))) {
                    var keep=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach(string name in new[]{"current.json","previous.json"}) {
                        string pointer=Path.Combine(updates,name);
                        if(!File.Exists(pointer))continue;
                        UpdateFiles.NoLinks(pointer);
                        string folder=UpdateFiles.Read<CurrentRelease>(pointer).Folder;
                        if(!Regex.IsMatch(folder,"^version-[0-9a-f]{32}$"))throw new IOException("Invalid cache pointer");
                        keep.Add(folder);
                    }
                    foreach(string path in Directory.EnumerateDirectories(updates)) {
                        string name=Path.GetFileName(path);
                        if(Regex.IsMatch(name,"^version-[0-9a-f]{32}$") && !keep.Contains(name) &&
                           !runningPaths.Any(p=>Path.GetFullPath(p).StartsWith(path+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))Remove(path);
                    }
                    string downloads=Path.Combine(updates,"downloads");
                    if(Directory.Exists(downloads)) {
                        UpdateFiles.NoLinks(downloads);
                        foreach(string path in Directory.EnumerateDirectories(downloads))
                            if(Regex.IsMatch(Path.GetFileName(path),"^[0-9a-f]{32}$") && Directory.GetLastWriteTimeUtc(path)<DateTime.UtcNow.AddDays(-1))Remove(path);
                    }
                    foreach(string path in Directory.EnumerateFiles(updates))
                        if(Regex.IsMatch(Path.GetFileName(path),"^(ack-[0-9a-f]{32}|(activation|rollback|failed)-[0-9a-f]{32}\\.json)$") && File.GetLastWriteTimeUtc(path)<DateTime.UtcNow.AddDays(-1))Remove(path);
                }
            } catch(IOException) {} catch(UnauthorizedAccessException) {} catch(JsonException) {}
        }
        var stale=Directory.EnumerateFiles(data,"session-*.ini").Where(p=> {
            var match=Regex.Match(Path.GetFileName(p),"^session-([0-9]+)\\.ini$");
            return match.Success && int.TryParse(match.Groups[1].Value,out int pid) && !processAlive(pid);
        }).OrderByDescending(File.GetLastWriteTimeUtc).Skip(2).ToArray();
        foreach(string path in stale)Remove(path);
        return freed;
    }
    internal static void RemoveCompletedDownload(string request)
    {
        try {
            string downloads=Path.GetFullPath(Path.Combine(UpdateFiles.Root,"downloads"));
            string directory=Path.GetDirectoryName(Path.GetFullPath(request))!;
            if(!string.Equals(Path.GetDirectoryName(directory),downloads,StringComparison.OrdinalIgnoreCase) ||
               !Regex.IsMatch(Path.GetFileName(directory),"^[0-9a-f]{32}$"))return;
            UpdateFiles.NoLinks(directory);
            foreach(string name in new[]{"package.zip","request.json"}) {
                string path=Path.Combine(directory,name);UpdateFiles.NoLinks(path);if(File.Exists(path))File.Delete(path);
            }
            if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);
        } catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    internal static long Run(string? directory=null)
    {
        try {
            string data=directory??Path.GetDirectoryName(UpdateFiles.Root)!;
            if(!Directory.Exists(data))return 0;
            var paths=new List<string>();
            foreach(string name in new[]{"ForceBond","ForceBond.Updater","OdysseyMP.Loader"})
                foreach(var process in Process.GetProcessesByName(name))using(process) {
                    // Unknown live process path: skip cleanup rather than guess.
                    try {if(process.MainModule?.FileName is string path)paths.Add(path);else return 0;}
                    catch {return 0;}
                }
            return Clean(data,paths.ToArray(),pid=> {
                try {using var process=Process.GetProcessById(pid);return !process.HasExited;}
                catch(ArgumentException){return false;} catch{return true;}
            });
        } catch {return 0;}
    }
}
