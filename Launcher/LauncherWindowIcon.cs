using System.Reflection;
using System.Runtime.InteropServices;

namespace ForceBond.Launcher;

// Keep the streams AND HICON owners alive while Windows may ask for the window
// icon. Borderless forms also need explicit small/big icons after HWND changes.
internal sealed class LauncherWindowIcon : NativeWindow, IDisposable
{
    private readonly Form _form;
    private readonly byte[] _data;
    private IconFrame? _frame;
    private bool _disposed;

    internal LauncherWindowIcon(Form form)
    {
        _form = form;
        using var resource = typeof(LauncherWindowIcon).Assembly.GetManifestResourceStream("ForceBond.Launcher.force-bond.ico")
            ?? throw new InvalidOperationException("Embedded launcher icon is missing.");
        using var buffer = new MemoryStream(); resource.CopyTo(buffer); _data = buffer.ToArray();
        _form.ShowIcon = true;
        Rebuild(96);
        _form.HandleCreated += HandleCreated;
        _form.HandleDestroyed += HandleDestroyed;
        _form.Activated += Refresh;
        _form.Deactivate += Refresh;
        _form.DpiChanged += DpiChanged;
        if (_form.IsHandleCreated) Attach();
    }

    internal static void SetApplicationIdentity()
    {
        int result = SetCurrentProcessExplicitAppUserModelID("SyndicateCorp.ForceBond.Launcher");
        if (result < 0) LauncherUpdates.Log($"Taskbar identity: 0x{result:X8}");
    }

    private void HandleCreated(object? sender, EventArgs e) => Attach();
    private void HandleDestroyed(object? sender, EventArgs e) { if (Handle != IntPtr.Zero) ReleaseHandle(); }
    private void Refresh(object? sender, EventArgs e) => Apply();
    private void DpiChanged(object? sender, DpiChangedEventArgs e) { Rebuild(e.DeviceDpiNew); Apply(); }
    private void Attach()
    {
        if (_disposed) return;
        if (Handle != IntPtr.Zero) ReleaseHandle();
        AssignHandle(_form.Handle);
        Rebuild(_form.DeviceDpi);
        Apply();
    }
    private void Rebuild(int dpi)
    {
        if (_disposed) return;
        int small = Math.Clamp(GetSystemMetricsForDpi(49, (uint)Math.Max(96, dpi)), 16, 256);
        int large = Math.Clamp(GetSystemMetricsForDpi(11, (uint)Math.Max(96, dpi)), 32, 256);
        var next = new IconFrame(_data, small, large);
        var old = _frame;
        _frame = next;
        _form.Icon = next.Large;
        Apply();
        old?.Dispose();
    }
    private void Apply()
    {
        if (_disposed || Handle == IntPtr.Zero || _frame is null) return;
        SendMessage(Handle, 0x80, IntPtr.Zero, _frame.Small.Handle); // WM_SETICON / ICON_SMALL
        SendMessage(Handle, 0x80, (IntPtr)1, _frame.Large.Handle);
    }
    protected override void WndProc(ref Message m)
    {
        if (!_disposed && _frame is not null && m.Msg == 0x7f && m.WParam.ToInt64() is >= 0 and <= 2)
        {
            m.Result = m.WParam == (IntPtr)1 ? _frame.Large.Handle : _frame.Small.Handle;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg is 0x7e or 0x31e) Apply(); // Display / desktop composition changes.
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _form.HandleCreated -= HandleCreated; _form.HandleDestroyed -= HandleDestroyed;
        _form.Activated -= Refresh; _form.Deactivate -= Refresh; _form.DpiChanged -= DpiChanged;
        if (Handle != IntPtr.Zero) ReleaseHandle();
        _frame?.Dispose(); _frame = null;
    }
    private sealed class IconFrame : IDisposable
    {
        private readonly MemoryStream _smallStream, _largeStream;
        internal Icon Small { get; }
        internal Icon Large { get; }
        internal IconFrame(byte[] data, int small, int large)
        {
            _smallStream = new MemoryStream(data, false); _largeStream = new MemoryStream(data, false);
            Small = new Icon(_smallStream, new Size(small, small));
            Large = new Icon(_largeStream, new Size(large, large));
            _ = Small.Handle; _ = Large.Handle;
        }
        public void Dispose() { Small.Dispose(); Large.Dispose(); _smallStream.Dispose(); _largeStream.Dispose(); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
