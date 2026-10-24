using System;
using System.Runtime.InteropServices;
using System.Text;

internal static class Win32
{
    // ---------------- delegates ----------------
    public delegate bool EnumDesktopProc([MarshalAs(UnmanagedType.LPWStr)] string lpszDesktopName, IntPtr lParam);
    public delegate bool EnumWndProc(IntPtr hwnd, IntPtr lParam);

    // ---------------- desktops / window station ----------------
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenDesktopW(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateDesktopW(string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode, uint dwFlags, uint dwDesiredAccess, IntPtr lpsa);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool EnumDesktopsW(IntPtr hWinSta, EnumDesktopProc cb, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWndProc cb, IntPtr lParam);

    // ---------------- windows ----------------
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, StringBuilder sb, int maxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint nCmd);

    [DllImport("user32.dll")]
    public static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumChildWindows(IntPtr parent, EnumWndProc cb, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(IntPtr hWnd, uint msg, UIntPtr wp, IntPtr lp);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, UIntPtr wp, IntPtr lp, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    [DllImport("user32.dll")]
    public static extern IntPtr GetMenu(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern IntPtr GetSubMenu(IntPtr hMenu, int pos);

    [DllImport("user32.dll")]
    public static extern uint GetMenuItemID(IntPtr hMenu, int pos);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetMenuStringW(IntPtr hMenu, uint uIDItem, StringBuilder sb, int cchMax, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // ---------------- gdi ----------------
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);

    // ---------------- process / cross-process memory ----------------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcessW(string lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFOW lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr nSize, IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, IntPtr nSize, IntPtr lpNumberOfBytesRead);

    // ---------------- constants ----------------
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public const uint WM_COMMAND = 0x0111;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;

    public const uint LB_GETCOUNT = 0x018B;
    public const uint LB_GETTEXT = 0x0189;
    public const uint LB_GETTEXTLEN = 0x018A;  // verified vs winuser.h (0x018C is LB_SELECTSTRING!)
    public const uint LB_SETCURSEL = 0x0186;

    public const uint CB_GETCOUNT = 0x0146;
    public const uint CB_SETCURSEL = 0x014E;   // verified vs winuser.h (0x0143 is CB_ADDSTRING!)
    public const uint CB_GETLBTEXT = 0x0148;
    public const uint CB_GETLBTEXTLEN = 0x0149;

    public const uint TVM_GETNEXTITEM = 0x110A;
    public const uint TVM_SELECTITEM = 0x110B;
    public const uint TVM_GETITEMW = 0x113E;
    public const uint TVGN_ROOT = 0;
    public const uint TVGN_NEXT = 1;
    public const uint TVGN_CHILD = 4;
    public const uint TVGN_CARET = 9;
    public const uint TVIF_TEXT = 0x0001;
    public const uint TVIF_HANDLE = 0x0010;

    public const uint LVM_GETITEMCOUNT = 0x1004;
    public const uint LVM_SETITEMSTATE = 0x102B;   // LVM_FIRST + 43 (verified against commctrl.h 10.0.26100)
    public const uint LVM_GETITEMTEXTW = 0x1073;   // LVM_FIRST + 115
    public const uint LVIF_TEXT = 0x0001;
    public const uint LVIF_STATE = 0x0008;
    public const uint LVIS_SELECTED = 0x0002;
    public const uint LVIS_FOCUSED = 0x0001;

    public const uint LBN_SELCHANGE = 1;
    public const uint CBN_SELCHANGE = 1;
    public const uint GW_OWNER = 4;

    public const uint MF_BYPOSITION = 0x400;

    // desktop access: READOBJECTS|CREATEWINDOW|CREATEMENU|HOOKCONTROL|JOURNALRECORD|JOURNALPLAYBACK|ENUMERATE|WRITEOBJECTS
    public const uint DESKTOP_RW = 0xFF;
    public const uint DESKTOP_GENERIC_ALL = 0x10000000;

    public const uint PROCESS_VM = 0x38;             // VM_OPERATION|VM_READ|VM_WRITE
    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint SYNCHRONIZE = 0x00100000;
    public const uint QUERY_LIMITED = 0x1000;
    public const uint MEM_COMMIT_RESERVE = 0x3000;
    public const uint PAGE_READWRITE = 0x04;
    public const uint MEM_RELEASE = 0x8000;
    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint WAIT_TIMEOUT = 0x00000102;

    public const uint PW_RENDERFULLCONTENT = 2;
    public const uint DIB_RGB_COLORS = 0;
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int L, T, R, B;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct STARTUPINFOW
{
    public uint cb;
    [MarshalAs(UnmanagedType.LPWStr)] public string lpReserved;
    [MarshalAs(UnmanagedType.LPWStr)] public string lpDesktop;
    [MarshalAs(UnmanagedType.LPWStr)] public string lpTitle;
    public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
    public ushort wShowWindow, cbReserved2;
    public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
}

[StructLayout(LayoutKind.Sequential)]
public struct PROCESS_INFORMATION
{
    public IntPtr hProcess, hThread;
    public uint dwProcessId, dwThreadId;
}

[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth, biHeight;
    public ushort biPlanes, biBitCount;
    public uint biCompression, biSizeImage;
    public int biXPelsPerMeter, biYPelsPerMeter;
    public uint biClrUsed, biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
public struct TVITEMW
{
    public uint mask;
    public IntPtr hItem;
    public uint state, stateMask;
    public IntPtr pszText;
    public int cchTextMax, iImage, iSelectedImage, cChildren;
    public IntPtr lParam;
}

[StructLayout(LayoutKind.Sequential)]
public struct LVITEMW
{
    public uint mask;
    public int iItem;
    public uint iSubItem, state, stateMask;
    public IntPtr pszText;
    public int cchTextMax, iImage;
    public IntPtr lParam;
    public int iIndent, iGroupId;
    public uint cColumns;
    public IntPtr puColumns, piColFmt;
    public int iGroup;
}
