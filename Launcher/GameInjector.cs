using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ForceBond.Launcher;

internal static class GameInjector
{
    private const uint ProcessCreateThread = 0x0002;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;
    private const uint WaitObject0 = 0;
    private const int TimeoutMilliseconds = 15_000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size,
        uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address,
        byte[] buffer, UIntPtr size, out UIntPtr bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes,
        UIntPtr stackSize, IntPtr startAddress, IntPtr parameter, uint flags, out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    internal static void Inject(Process game, string dllPath)
    {
        if (Environment.Is64BitProcess)
            throw new InvalidOperationException("The Force Bond launcher must run as 32-bit x86.");
        if (!File.Exists(dllPath)) throw new FileNotFoundException("The Force Bond game module is missing.", dllPath);

        IntPtr process = IntPtr.Zero;
        IntPtr remotePath = IntPtr.Zero;
        IntPtr thread = IntPtr.Zero;
        try
        {
            process = OpenProcess(ProcessCreateThread | ProcessQueryInformation |
                ProcessVmOperation | ProcessVmWrite, false, game.Id);
            if (process == IntPtr.Zero) throw LastError("OpenProcess");

            IntPtr localKernel32 = GetModuleHandle("kernel32.dll");
            IntPtr localLoadLibrary = GetProcAddress(localKernel32, "LoadLibraryW");
            if (localKernel32 == IntPtr.Zero || localLoadLibrary == IntPtr.Zero)
                throw LastError("Resolve LoadLibraryW");

            IntPtr remoteLoadLibrary = ResolveRemoteProcedure(game, localLoadLibrary);
            byte[] pathBytes = Encoding.Unicode.GetBytes(Path.GetFullPath(dllPath) + '\0');
            remotePath = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)pathBytes.Length,
                MemCommit | MemReserve, PageReadWrite);
            if (remotePath == IntPtr.Zero) throw LastError("VirtualAllocEx");

            if (!WriteProcessMemory(process, remotePath, pathBytes,
                    (UIntPtr)pathBytes.Length, out UIntPtr written) ||
                written.ToUInt64() != (ulong)pathBytes.Length)
                throw LastError("WriteProcessMemory");

            thread = CreateRemoteThread(process, IntPtr.Zero, UIntPtr.Zero,
                remoteLoadLibrary, remotePath, 0, out _);
            if (thread == IntPtr.Zero) throw LastError("CreateRemoteThread");

            uint wait = WaitForSingleObject(thread, TimeoutMilliseconds);
            if (wait != WaitObject0)
                throw wait == 258
                    ? new TimeoutException("The game connector did not finish loading within 15 seconds.")
                    : LastError("WaitForSingleObject");
            if (!GetExitCodeThread(thread, out uint moduleHandle)) throw LastError("GetExitCodeThread");
            if (moduleHandle == 0) throw new InvalidOperationException("The game refused to load the Force Bond module.");
        }
        finally
        {
            if (thread != IntPtr.Zero) CloseHandle(thread);
            if (remotePath != IntPtr.Zero && process != IntPtr.Zero)
                VirtualFreeEx(process, remotePath, UIntPtr.Zero, MemRelease);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    private static IntPtr ResolveRemoteProcedure(Process game, IntPtr localProcedure)
    {
        using Process current = Process.GetCurrentProcess();
        ProcessModule? owner = null;
        long address = localProcedure.ToInt64();
        foreach (ProcessModule module in current.Modules)
        {
            long start = module.BaseAddress.ToInt64();
            if (address >= start && address < start + module.ModuleMemorySize)
            {
                owner = module;
                break;
            }
        }
        if (owner is null) throw new InvalidOperationException("Could not locate LoadLibraryW in the launcher.");

        long rva = address - owner.BaseAddress.ToInt64();
        foreach (ProcessModule module in game.Modules)
        {
            if (string.Equals(module.ModuleName, owner.ModuleName, StringComparison.OrdinalIgnoreCase))
                return new IntPtr(checked(module.BaseAddress.ToInt64() + rva));
        }
        throw new InvalidOperationException($"The game does not have the required Windows module: {owner.ModuleName}.");
    }

    private static Win32Exception LastError(string operation) =>
        new(Marshal.GetLastWin32Error(), operation);
}
