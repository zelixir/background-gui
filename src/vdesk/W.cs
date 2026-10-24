using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

internal sealed class UsageError : Exception
{
    public UsageError(string m) : base(m) { }
}

internal sealed class ToolError : Exception
{
    public readonly JObj Payload;

    public ToolError(string msg) : base(msg)
    {
        Payload = new JObj { ["error"] = new JStr(msg) };
    }

    public ToolError(JObj payload, string msg) : base(msg)
    {
        Payload = payload;
    }

    public static ToolError Win32(string msg)
    {
        int e = Marshal.GetLastWin32Error();
        return new ToolError(msg + " (Win32 error " + e + ")");
    }
}

internal sealed class Args
{
    public readonly List<string> Pos = new List<string>();
    readonly Dictionary<string, string> _opts = new Dictionary<string, string>(StringComparer.Ordinal);
    readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);

    public static Args Parse(string[] tokens)
    {
        var r = new Args();
        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i];
            if (t.Length > 2 && t.StartsWith("--"))
            {
                string name = t.Substring(2);
                if (i + 1 < tokens.Length && !(tokens[i + 1].Length > 2 && tokens[i + 1].StartsWith("--")))
                {
                    r._opts[name] = tokens[i + 1];
                    i++;
                }
                else r._flags.Add(name);
            }
            else r.Pos.Add(t);
        }
        return r;
    }

    public bool Has(string flag) { return _flags.Contains(flag); }

    public string Req(string name)
    {
        string v;
        if (!_opts.TryGetValue(name, out v) || v == null || v.Length == 0)
            throw new UsageError("missing required option --" + name);
        return v;
    }

    public string Opt(string name, string def = null)
    {
        string v;
        return _opts.TryGetValue(name, out v) ? v : def;
    }

    public int OptInt(string name, int def)
    {
        string s = Opt(name);
        if (s == null) return def;
        int v;
        if (!int.TryParse(s, out v)) throw new UsageError("--" + name + ": expected integer, got \"" + s + "\"");
        return v;
    }

    public double OptDouble(string name, double def)
    {
        string s = Opt(name);
        if (s == null) return def;
        double v;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            throw new UsageError("--" + name + ": expected number, got \"" + s + "\"");
        return v;
    }

    public int ReqInt(string name)
    {
        string s = Req(name);
        int v;
        if (!int.TryParse(s, out v)) throw new UsageError("--" + name + ": expected integer, got \"" + s + "\"");
        return v;
    }
}

internal static class W
{
    public static long L(UIntPtr u) { return (long)u.ToUInt64(); }
    public static IntPtr P(UIntPtr u) { return new IntPtr((long)u.ToUInt64()); }

    /// SendMessageTimeoutW wrapper: SMTO_ABORTIFHUNG always on; result is read from lpdwResult (not the return value).
    public static UIntPtr Smt(IntPtr h, uint msg, ulong wp, long lp, uint timeoutMs = 3000)
    {
        IntPtr ok = Win32.SendMessageTimeoutW(h, msg, (UIntPtr)wp, (IntPtr)lp, Win32.SMTO_ABORTIFHUNG, timeoutMs, out UIntPtr r);
        return ok == IntPtr.Zero ? UIntPtr.Zero : r;
    }

    /// Like Smt but distinguishes "message timed out / hung" from a real result.
    public static bool SmtR(IntPtr h, uint msg, ulong wp, long lp, out long result, uint timeoutMs = 3000)
    {
        IntPtr ok = Win32.SendMessageTimeoutW(h, msg, (UIntPtr)wp, (IntPtr)lp, Win32.SMTO_ABORTIFHUNG, timeoutMs, out UIntPtr r);
        result = (long)r.ToUInt64();
        return ok != IntPtr.Zero;
    }

    public static bool Post(IntPtr h, uint msg, ulong wp, long lp)
    {
        return Win32.PostMessageW(h, msg, (UIntPtr)wp, (IntPtr)lp);
    }

    public static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        Win32.GetClassNameW(h, sb, 256);
        return sb.ToString();
    }

    public static int PidOf(IntPtr h)
    {
        uint p;
        Win32.GetWindowThreadProcessId(h, out p);
        return unchecked((int)p);
    }

    public static string TextOf(IntPtr h, uint timeoutMs = 3000)
    {
        long n = L(Smt(h, Win32.WM_GETTEXTLENGTH, 0, 0, timeoutMs));
        if (n <= 0) return "";
        if (n > 65536) n = 65536;
        IntPtr buf = Marshal.AllocHGlobal((int)((n + 2) * 2));
        try
        {
            Smt(h, Win32.WM_GETTEXT, (ulong)(n + 1), (long)buf, timeoutMs);
            return Marshal.PtrToStringUni(buf) ?? "";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static JArr RectOf(IntPtr h)
    {
        RECT rc;
        Win32.GetWindowRect(h, out rc);
        return new JArr(new JNum(rc.L), new JNum(rc.T), new JNum(rc.R - rc.L), new JNum(rc.B - rc.T));
    }

    public static List<IntPtr> EnumTop(IntPtr hdesk)
    {
        var list = new List<IntPtr>();
        Win32.EnumWndProc cb = null;
        cb = (h, l) => { list.Add(h); return true; };
        Win32.EnumDesktopWindows(hdesk, cb, IntPtr.Zero);
        return list;
    }

    public static List<IntPtr> EnumChildren(IntPtr parent)
    {
        var list = new List<IntPtr>();
        Win32.EnumWndProc cb = null;
        cb = (h, l) => { list.Add(h); return true; };
        Win32.EnumChildWindows(parent, cb, IntPtr.Zero);
        return list;
    }

    public static IntPtr HwndArg(Args a)
    {
        string s = a.Req("hwnd");
        long v;
        if (!TryHandle(s, out v)) throw new UsageError("--hwnd: invalid handle \"" + s + "\"");
        return new IntPtr(v);
    }

    public static IntPtr HwndOrErr(Args a)
    {
        return Href.Open(a).H; // legacy helper; prefer Href.Open with using
    }

    /// Locate the desktop that contains hwnd and open it with DESKTOP_RW.
    /// A window handle is only usable while the calling thread holds an open handle to its owning
    /// desktop (verified: IsWindow / GetWindowThreadProcessId / SendMessage all fail for a
    /// cross-desktop handle once the desktop handle is closed). Caller must CloseDesktop the result.
    public static IntPtr OpenDesktopOf(IntPtr hwnd)
    {
        IntPtr found = IntPtr.Zero;
        Win32.EnumDesktopProc cb = null;
        cb = (name, lp) =>
        {
            IntPtr hd = Win32.OpenDesktopW(name, 0, false, Win32.DESKTOP_RW);
            if (hd == IntPtr.Zero) return true; // inaccessible desktop (Winlogon etc.) - skip
            bool hit = false;
            Win32.EnumWndProc wcb = null;
            wcb = (w, l) =>
            {
                if (w == hwnd) { hit = true; return false; }
                // hwnd may be a descendant (child control): EnumChildWindows walks all generations
                bool childHit = false;
                Win32.EnumWndProc ccb = null;
                ccb = (c, l2) => { if (c == hwnd) { childHit = true; return false; } return true; };
                Win32.EnumChildWindows(w, ccb, IntPtr.Zero);
                if (childHit) { hit = true; return false; }
                return true;
            };
            Win32.EnumDesktopWindows(hd, wcb, IntPtr.Zero);
            if (hit) { found = hd; return false; }
            Win32.CloseDesktop(hd);
            return true;
        };
        // NB: EnumDesktopsW returns FALSE whenever the callback returns FALSE (including our
        // intentional early stop), so the return value is not usable as an error signal here.
        Win32.EnumDesktopsW(Win32.GetProcessWindowStation(), cb, IntPtr.Zero);
        if (found == IntPtr.Zero)
            throw new ToolError("window not found on any desktop of the current window station: hwnd=" + hwnd);
        return found;
    }

    public static bool TryHandle(string s, out long v)
    {
        v = 0;
        if (string.IsNullOrEmpty(s)) return false;
        try
        {
            if (s.StartsWith("0x") || s.StartsWith("0X")) v = Convert.ToInt64(s.Substring(2), 16);
            else v = Convert.ToInt64(s, 10);
            return v != 0;
        }
        catch { return false; }
    }
}

/// An hwnd plus its owning desktop kept open for the duration of a command.
/// Usage: using (Href hr = Href.Open(a)) { IntPtr h = hr.H; ... }
internal sealed class Href : IDisposable
{
    public readonly IntPtr H;
    readonly IntPtr _hd;

    Href(IntPtr h, IntPtr hd) { H = h; _hd = hd; }

    public static Href Open(Args a)
    {
        IntPtr h = W.HwndArg(a);
        IntPtr hd = W.OpenDesktopOf(h);
        return new Href(h, hd);
    }

    public void Dispose()
    {
        if (_hd != IntPtr.Zero) Win32.CloseDesktop(_hd);
    }
}
