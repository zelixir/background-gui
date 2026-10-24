using System;
using System.Runtime.InteropServices;
using System.Text;

/// Cross-process memory helper: OpenProcess(VM_OPERATION|VM_READ|VM_WRITE) + VirtualAllocEx/Read/Write/FreeEx.
internal sealed class XMem : IDisposable
{
    public IntPtr H;

    public XMem(int pid)
    {
        H = Win32.OpenProcess(Win32.PROCESS_VM, false, pid);
        if (H == IntPtr.Zero)
            throw ToolError.Win32("cannot open process " + pid + " for cross-process memory access");
    }

    public IntPtr Alloc(int cb)
    {
        IntPtr p = Win32.VirtualAllocEx(H, IntPtr.Zero, (IntPtr)cb, Win32.MEM_COMMIT_RESERVE, Win32.PAGE_READWRITE);
        if (p == IntPtr.Zero) throw ToolError.Win32("VirtualAllocEx(" + cb + " bytes) failed in pid " + Pid);
        return p;
    }

    public int Pid { get { return _pid; } set { _pid = value; } }
    int _pid;

    public void Write(IntPtr remote, byte[] data)
    {
        if (!Win32.WriteProcessMemory(H, remote, data, (IntPtr)data.Length, IntPtr.Zero))
            throw ToolError.Win32("WriteProcessMemory failed (pid " + _pid + ")");
    }

    public byte[] Read(IntPtr remote, int cb)
    {
        var b = new byte[cb];
        if (!Win32.ReadProcessMemory(H, remote, b, (IntPtr)cb, IntPtr.Zero))
            throw ToolError.Win32("ReadProcessMemory failed (pid " + _pid + ")");
        return b;
    }

    public string ReadWString(IntPtr remote, int maxChars)
    {
        byte[] b = Read(remote, maxChars * 2);
        int chars = maxChars;
        for (int i = 0; i < maxChars; i++)
        {
            if (b[2 * i] == 0 && b[2 * i + 1] == 0) { chars = i; break; }
        }
        return Encoding.Unicode.GetString(b, 0, chars * 2);
    }

    public void Free(IntPtr p)
    {
        Win32.VirtualFreeEx(H, p, IntPtr.Zero, Win32.MEM_RELEASE);
    }

    public void Dispose()
    {
        if (H != IntPtr.Zero) { Win32.CloseHandle(H); H = IntPtr.Zero; }
    }
}
