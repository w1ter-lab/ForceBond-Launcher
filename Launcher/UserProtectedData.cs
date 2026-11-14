using System.Runtime.InteropServices;
namespace ForceBond.Launcher;
internal static class UserProtectedData
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptProtectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    internal static byte[] Protect(byte[] data)=>Transform(data,true);
    internal static byte[] Unprotect(byte[] data)=>Transform(data,false);
    private static byte[] Transform(byte[] data,bool protect)
    {
        var input=new Blob { Length=data.Length,Data=Marshal.AllocHGlobal(data.Length) };Blob output=default;
        try
        {
            Marshal.Copy(data,0,input.Data,data.Length);
            bool ok=protect?CryptProtectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new IOException("Could not protect the access session.");
            byte[] result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally
        {
            for(int i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);
            if(output.Data!=IntPtr.Zero) { for(int i=0;i<output.Length;i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data); }
        }
    }
}
