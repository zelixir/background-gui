using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static partial class Cmds
{
    public const string UsageAll =
@"vdesk - hidden-desktop GUI automation (message-level, no input injection)

usage:
  vdesk desktop list
  vdesk desktop create <name>
  vdesk desktop destroy <name> [--kill]
  vdesk launch --desktop <name> --exe <path> [--args <string>] [--cwd <dir>]
               [--wait-class <substr>] [--wait-title <substr>] [--timeout <sec>]
  vdesk windows --desktop <name> [--pid <n>] [--class <substr>] [--title <substr>] [--hidden]
  vdesk controls --hwnd <n>
  vdesk text --hwnd <n>
  vdesk menu --hwnd <n>
  vdesk invoke --hwnd <n> (--cmdid <n> | --menu ""A>B"")
  vdesk select --hwnd <n> (--index <n> | --text <substr>)
  vdesk items --hwnd <n>
  vdesk shot --hwnd <n> --out <file.png>
  vdesk close --hwnd <n>
  vdesk kill --pid <n> [--timeout <sec>]

All results: UTF-8 JSON on stdout. Runtime errors: {""error"":...} on stderr, exit 3.
Usage errors: exit 2. hwnd values are decimal strings.
This tool never injects mouse/keyboard input and refuses to destroy the ""Default"" desktop.";

    public static int Run(string[] argv)
    {
        if (argv.Length == 0) throw new UsageError("missing command; try: vdesk help");
        switch (argv[0])
        {
            case "help":
            case "--help":
            case "-h":
            case "/?":
                Console.Out.WriteLine(UsageAll);
                return 0;
            case "desktop": return Desktop(argv);
            case "launch": return Launch(ArgsFrom(argv, 1));
            case "windows": return Windows(ArgsFrom(argv, 1));
            case "controls": return Controls(ArgsFrom(argv, 1));
            case "text": return Text(ArgsFrom(argv, 1));
            case "menu": return Menu(ArgsFrom(argv, 1));
            case "invoke": return Invoke(ArgsFrom(argv, 1));
            case "select": return Select(ArgsFrom(argv, 1));
            case "items": return Items(ArgsFrom(argv, 1));
            case "shot": return Shot(ArgsFrom(argv, 1));
            case "close": return Close(ArgsFrom(argv, 1));
            case "kill": return Kill(ArgsFrom(argv, 1));
            default: throw new UsageError("unknown command \"" + argv[0] + "\"; try: vdesk help");
        }
    }

    static Args ArgsFrom(string[] a, int start)
    {
        var t = new string[a.Length - start];
        Array.Copy(a, start, t, 0, t.Length);
        return Args.Parse(t);
    }

    // ---------------- desktop list / create / destroy ----------------

    static int Desktop(string[] a)
    {
        if (a.Length < 2) throw new UsageError("desktop: missing subcommand (list|create|destroy)");
        Args args = ArgsFrom(a, 2);
        switch (a[1])
        {
            case "list": return DesktopList(args);
            case "create": return DesktopCreate(args);
            case "destroy": return DesktopDestroy(args);
            default: throw new UsageError("desktop: unknown subcommand \"" + a[1] + "\"");
        }
    }

    static int DesktopList(Args a)
    {
        var names = new JArr();
        Win32.EnumDesktopProc cb = null;
        cb = (name, lp) => { names.Items.Add(new JStr(name)); return true; };
        if (!Win32.EnumDesktopsW(Win32.GetProcessWindowStation(), cb, IntPtr.Zero))
            throw ToolError.Win32("EnumDesktopsW failed");
        Json.Out(new JObj { ["desktops"] = names });
        return 0;
    }

    static int DesktopCreate(Args a)
    {
        if (a.Pos.Count < 1) throw new UsageError("desktop create: missing <name>");
        string name = a.Pos[0];
        IntPtr h = Win32.OpenDesktopW(name, 0, false, Win32.DESKTOP_RW);
        if (h != IntPtr.Zero)
        {
            Win32.CloseDesktop(h);
            Json.Out(new JObj { ["name"] = new JStr(name), ["created"] = new JBool(false) });
            return 0;
        }
        h = Win32.CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, Win32.DESKTOP_GENERIC_ALL, IntPtr.Zero);
        if (h == IntPtr.Zero) throw ToolError.Win32("CreateDesktopW(\"" + name + "\") failed");
        Win32.CloseDesktop(h);
        Json.Out(new JObj
        {
            ["name"] = new JStr(name),
            ["created"] = new JBool(true),
            ["note"] = new JStr("an empty desktop with no attached process/handle may be reclaimed by the system; launch re-creates it on demand")
        });
        return 0;
    }

    static int DesktopDestroy(Args a)
    {
        if (a.Pos.Count < 1) throw new UsageError("desktop destroy: missing <name>");
        string name = a.Pos[0];
        if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
            throw new ToolError("refusing to destroy the default desktop");
        bool kill = a.Has("kill");

        IntPtr h = Win32.OpenDesktopW(name, 0, false, Win32.DESKTOP_RW);
        if (h == IntPtr.Zero)
            throw ToolError.Win32("desktop \"" + name + "\" does not exist (or cannot be opened)");

        var wins = W.EnumTop(h);
        var pids = new SortedSet<int>();
        foreach (var w in wins) pids.Add(W.PidOf(w));

        int killed = 0;
        var survivors = new JArr();
        if (kill)
        {
            foreach (int pid in pids)
            {
                IntPtr ph = Win32.OpenProcess(Win32.SYNCHRONIZE | Win32.PROCESS_TERMINATE | Win32.QUERY_LIMITED, false, pid);
                if (ph == IntPtr.Zero) { survivors.Items.Add(new JNum(pid)); continue; }
                Win32.TerminateProcess(ph, 1);
                if (Win32.WaitForSingleObject(ph, 8000) == Win32.WAIT_OBJECT_0) killed++;
                else survivors.Items.Add(new JNum(pid));
                Win32.CloseHandle(ph);
            }
        }

        bool closed = Win32.CloseDesktop(h);
        var o = new JObj { ["name"] = new JStr(name), ["destroyed"] = new JBool(closed), ["windows"] = new JNum(wins.Count) };
        if (kill)
        {
            o["processes"] = new JNum(pids.Count);
            o["killed"] = new JNum(killed);
        }
        if (survivors.Items.Count > 0) o["survivors"] = survivors;
        if (!closed) o["note"] = new JStr("CloseDesktop reported failure; a process may still be attached");
        Json.Out(o);
        return 0;
    }

    // ---------------- launch ----------------

    static int Launch(Args a)
    {
        string desktop = a.Req("desktop");
        string exe = a.Req("exe");
        string appArgs = a.Opt("args");
        string cwd = a.Opt("cwd");
        string waitClass = a.Opt("wait-class");
        string waitTitle = a.Opt("wait-title");
        double timeoutSec = a.OptDouble("timeout", 30);
        bool filtered = waitClass != null || waitTitle != null;

        // Open (or create) the desktop and hold the handle across CreateProcessW and the whole
        // polling window: an empty desktop is reclaimed by the system the moment its last handle /
        // attached process disappears (MSIX launcher stubs can exit within milliseconds).
        IntPtr hdesk = Win32.OpenDesktopW(desktop, 0, false, Win32.DESKTOP_RW);
        bool created = false;
        if (hdesk == IntPtr.Zero)
        {
            hdesk = Win32.CreateDesktopW(desktop, IntPtr.Zero, IntPtr.Zero, 0, Win32.DESKTOP_GENERIC_ALL, IntPtr.Zero);
            if (hdesk == IntPtr.Zero)
                throw ToolError.Win32("desktop \"" + desktop + "\" cannot be opened or created");
            created = true;
        }

        JObj found = null;
        JArr summary = new JArr();
        int pid = 0;
        try
        {
            var si = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>(), lpDesktop = desktop };
            var cmd = new StringBuilder("\"" + exe + "\"");
            if (appArgs != null && appArgs.Length > 0) cmd.Append(' ').Append(appArgs);
            if (!Win32.CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, cwd, ref si, out PROCESS_INFORMATION pi))
                throw ToolError.Win32("CreateProcessW failed for \"" + exe + "\" (lpDesktop=\"" + desktop + "\")");
            pid = unchecked((int)pi.dwProcessId);
            Win32.CloseHandle(pi.hThread);   // process/thread handles are not kept; later commands target by pid
            Win32.CloseHandle(pi.hProcess);

            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (true)
            {
                summary = new JArr();
                foreach (var w in W.EnumTop(hdesk))
                {
                    if (!Win32.IsWindowVisible(w)) continue;
                    int wpid = W.PidOf(w);
                    string cl = W.ClassOf(w);
                    string ti = W.TextOf(w);
                    summary.Items.Add(new JObj
                    {
                        ["hwnd"] = new JStr(w.ToString()),
                        ["pid"] = new JNum(wpid),
                        ["class"] = new JStr(cl),
                        ["title"] = new JStr(ti)
                    });

                    bool clsOk = waitClass == null || cl.IndexOf(waitClass, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool titleOk = waitTitle == null || ti.IndexOf(waitTitle, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!clsOk || !titleOk) continue;
                    bool ownerless = Win32.GetWindow(w, Win32.GW_OWNER) == IntPtr.Zero;
                    // with filters: accept the first ownerless window matching the filters (pid preferred;
                    // covers exec-alias/launcher scenarios where the spawned pid exits after activation).
                    // without filters: strictly the first visible ownerless window of the spawned pid.
                    bool accept = filtered
                        ? (wpid == pid || ownerless)
                        : (wpid == pid && ownerless);
                    if (!accept) continue;

                    found = new JObj
                    {
                        ["pid"] = new JNum(wpid),
                        ["hwnd"] = new JStr(w.ToString()),
                        ["class"] = new JStr(cl),
                        ["title"] = new JStr(ti)
                    };
                    if (wpid != pid)
                    {
                        found["launcher_pid"] = new JNum(pid);
                        found["note"] = new JStr("pid " + pid + " exposed no window (launcher/exec-alias); matched by class/title filter, real window pid is " + wpid);
                    }
                    break;
                }
                if (found != null) break;
                if (DateTime.UtcNow >= deadline) break;
                Thread.Sleep(250);
            }
        }
        finally { Win32.CloseDesktop(hdesk); }

        if (found == null)
        {
            var err = new JObj
            {
                ["error"] = new JStr("timeout (" + timeoutSec + "s): no matching window for pid " + pid + " on desktop \"" + desktop + "\""),
                ["pid"] = new JNum(pid),
                ["desktop"] = new JStr(desktop)
            };
            if (summary.Items.Count > 0) err["windows"] = summary;
            if (created) err["note"] = new JStr("desktop \"" + desktop + "\" was created by this command");
            throw new ToolError(err, "launch: window wait timeout");
        }
        if (created) found["desktop_created"] = new JBool(true);
        Json.Out(found);
        return 0;
    }

    // ---------------- close / kill ----------------

    static int Close(Args a)
    {
        using (Href hr = Href.Open(a))
        {
            IntPtr h = hr.H;
            if (!Win32.PostMessageW(h, Win32.WM_CLOSE, UIntPtr.Zero, IntPtr.Zero))
                throw ToolError.Win32("PostMessageW(WM_CLOSE) failed");
            Json.Out(new JObj { ["closed"] = new JBool(true), ["hwnd"] = new JStr(h.ToString()) });
        }
        return 0;
    }

    static int Kill(Args a)
    {
        int pid = a.ReqInt("pid");
        int timeoutSec = a.OptInt("timeout", 12);

        IntPtr ph = Win32.OpenProcess(Win32.SYNCHRONIZE | Win32.PROCESS_TERMINATE | Win32.QUERY_LIMITED, false, pid);
        if (ph == IntPtr.Zero) throw ToolError.Win32("cannot open process " + pid);

        bool graceful = Win32.WaitForSingleObject(ph, (uint)(timeoutSec * 1000)) == Win32.WAIT_OBJECT_0;
        if (!graceful)
        {
            if (!Win32.TerminateProcess(ph, 1))
            {
                int e = Marshal.GetLastWin32Error();
                Win32.CloseHandle(ph);
                throw new ToolError("TerminateProcess failed for pid " + pid + " (Win32 error " + e + ")");
            }
            Win32.WaitForSingleObject(ph, 5000);
        }
        Win32.CloseHandle(ph);
        Json.Out(new JObj { ["killed"] = new JBool(true), ["graceful"] = new JBool(graceful), ["pid"] = new JNum(pid) });
        return 0;
    }
}
