using System;
using System.Collections.Generic;
using System.Text;

internal static partial class Cmds
{
    // ---------------- windows ----------------

    static int Windows(Args a)
    {
        string desktop = a.Req("desktop");
        int pidFilter = a.OptInt("pid", 0);
        string clsF = a.Opt("class");
        string titleF = a.Opt("title");
        bool includeHidden = a.Has("hidden");

        IntPtr hd = Win32.OpenDesktopW(desktop, 0, false, Win32.DESKTOP_RW);
        if (hd == IntPtr.Zero)
            throw ToolError.Win32("cannot open desktop \"" + desktop + "\" (does it exist?)");

        var arr = new JArr();
        try
        {
            foreach (var w in W.EnumTop(hd))
            {
                int pid = W.PidOf(w);
                if (pidFilter != 0 && pid != pidFilter) continue;
                bool vis = Win32.IsWindowVisible(w);
                if (!vis && !includeHidden) continue;
                string cl = W.ClassOf(w);
                if (clsF != null && cl.IndexOf(clsF, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string ti = W.TextOf(w);
                if (titleF != null && ti.IndexOf(titleF, StringComparison.OrdinalIgnoreCase) < 0) continue;
                arr.Items.Add(new JObj
                {
                    ["hwnd"] = new JStr(w.ToString()),
                    ["class"] = new JStr(cl),
                    ["title"] = new JStr(ti),
                    ["pid"] = new JNum(pid),
                    ["visible"] = new JBool(vis),
                    ["rect"] = W.RectOf(w)
                });
            }
        }
        finally { Win32.CloseDesktop(hd); }
        Json.Out(new JObj { ["desktop"] = new JStr(desktop), ["windows"] = arr });
        return 0;
    }

    // ---------------- controls ----------------

    static int Controls(Args a)
    {
        using (Href hr = Href.Open(a))
        {
            IntPtr h = hr.H;
            var arr = new JArr();
            foreach (var c in W.EnumChildren(h))
            {
                arr.Items.Add(new JObj
                {
                    ["hwnd"] = new JStr(c.ToString()),
                    ["class"] = new JStr(W.ClassOf(c)),
                    ["text"] = new JStr(W.TextOf(c)),
                    ["id"] = new JNum(Win32.GetDlgCtrlID(c)),
                    ["visible"] = new JBool(Win32.IsWindowVisible(c)),
                    ["rect"] = W.RectOf(c)
                });
            }
            Json.Out(new JObj { ["hwnd"] = new JStr(h.ToString()), ["controls"] = arr });
        }
        return 0;
    }

    // ---------------- text ----------------

    static int Text(Args a)
    {
        using (Href hr = Href.Open(a))
        {
            IntPtr h = hr.H;
            Json.Out(new JObj { ["hwnd"] = new JStr(h.ToString()), ["text"] = new JStr(W.TextOf(h)) });
        }
        return 0;
    }

    // ---------------- menu ----------------

    static int Menu(Args a)
    {
        using (Href hr = Href.Open(a))
        {
            IntPtr h = hr.H;
            IntPtr hm = Win32.GetMenu(h);
            var arr = new JArr();
            if (hm != IntPtr.Zero) MenuLevel(hm, arr, 0);
            Json.Out(new JObj { ["hwnd"] = new JStr(h.ToString()), ["menus"] = arr });
        }
        return 0;
    }

    static void MenuLevel(IntPtr hmenu, JArr arr, int depth)
    {
        int n = Win32.GetMenuItemCount(hmenu);
        for (int i = 0; i < n; i++)
        {
            string txt = MenuText(hmenu, i);
            uint id = Win32.GetMenuItemID(hmenu, i);
            IntPtr sub = Win32.GetSubMenu(hmenu, i);
            var o = new JObj
            {
                ["index"] = new JNum(i),
                ["text"] = new JStr(txt),
                ["id"] = new JNum(id == 0xFFFFFFFFu ? -1 : id)
            };
            var subArr = new JArr();
            if (sub != IntPtr.Zero && depth + 1 < 3) MenuLevel(sub, subArr, depth + 1);
            o["sub"] = subArr;
            arr.Items.Add(o);
        }
    }

    internal static string MenuText(IntPtr hmenu, int pos)
    {
        var sb = new StringBuilder(512);
        uint n = Win32.GetMenuStringW(hmenu, (uint)pos, sb, sb.Capacity, Win32.MF_BYPOSITION);
        if (n <= 0) return "";
        if (n > 511) n = 511;
        return sb.ToString(0, (int)n);
    }
}
