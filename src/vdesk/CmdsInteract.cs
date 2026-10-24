using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal static partial class Cmds
{
    // ---------------- invoke ----------------

    static int Invoke(Args a)
    {
        using (Href hr = Href.Open(a))
        return InvokeInner(hr, a);
    }

    static int InvokeInner(Href hr, Args a)
    {
        IntPtr h = hr.H;
        string cmdidS = a.Opt("cmdid");
        string menuPath = a.Opt("menu");
        if (cmdidS != null && menuPath != null)
            throw new UsageError("invoke: --cmdid and --menu are mutually exclusive");
        if (cmdidS == null && menuPath == null)
            throw new UsageError("invoke: give --cmdid <n> or --menu \"segment>leaf\"");

        if (cmdidS != null)
        {
            long id;
            if (!long.TryParse(cmdidS, out id) || id < 0 || id > 0xFFFF)
                throw new UsageError("invoke: --cmdid expects an integer 0..65535");
            if (!W.Post(h, Win32.WM_COMMAND, (ulong)id, 0))
                throw ToolError.Win32("PostMessageW(WM_COMMAND) failed");
            Json.Out(new JObj { ["cmdid"] = new JNum(id), ["hwnd"] = new JStr(h.ToString()) });
            return 0;
        }

        var segs = new List<string>();
        foreach (string rawSeg in menuPath.Split(new[] { '>', '\uFF1E' }))
        {
            string s = rawSeg.Trim();
            if (s.Length > 0) segs.Add(s);
        }
        if (segs.Count == 0) throw new UsageError("invoke: --menu path is empty");

        IntPtr cur = Win32.GetMenu(h);
        if (cur == IntPtr.Zero) throw new ToolError("window " + h + " has no menu bar");

        var matched = new JArr();
        for (int s = 0; s < segs.Count; s++)
        {
            string seg = segs[s];
            int n = Win32.GetMenuItemCount(cur);
            if (n <= 0)
                throw MenuNotFound(segs, s, seg, matched, new JArr(), "menu level " + s + " is empty");

            int hit = -1;
            string hitLabel = null;
            var cands = new JArr();

            // pass 1: exact match (normalized, case-insensitive)
            for (int i = 0; i < n; i++)
            {
                string norm = NormMenu(MenuText(cur, i));
                if (norm.Length == 0) continue;
                if (string.Equals(norm, seg, StringComparison.OrdinalIgnoreCase)) { hit = i; hitLabel = norm; break; }
            }
            // pass 2: substring match (either direction; covers CN/EN labels and "(&O)" decorations)
            for (int i = 0; i < n && hit < 0; i++)
            {
                string norm = NormMenu(MenuText(cur, i));
                if (norm.Length == 0) continue;
                cands.Items.Add(new JStr(norm));
                if (norm.IndexOf(seg, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    seg.IndexOf(norm, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hit = i; hitLabel = norm;
                }
            }
            if (hit < 0)
            {
                // exact hit path skipped candidate collection; rebuild it for the error report
                if (cands.Items.Count == 0)
                    for (int i = 0; i < n; i++) { string norm = NormMenu(MenuText(cur, i)); if (norm.Length > 0) cands.Items.Add(new JStr(norm)); }
                throw MenuNotFound(segs, s, seg, matched, cands, "menu segment \"" + seg + "\" not found at level " + s);
            }

            matched.Items.Add(new JStr(hitLabel));
            IntPtr sub = Win32.GetSubMenu(cur, hit);
            if (s == segs.Count - 1)
            {
                if (sub != IntPtr.Zero)
                    throw MenuNotFound(segs, s, seg, matched, cands, "\"" + hitLabel + "\" is a submenu; the given path ends too early");
                uint id = Win32.GetMenuItemID(cur, hit);
                if (id == 0xFFFFFFFFu)
                    throw MenuNotFound(segs, s, seg, matched, cands, "\"" + hitLabel + "\" has no command id");
                if (!W.Post(h, Win32.WM_COMMAND, id, 0))
                    throw ToolError.Win32("PostMessageW(WM_COMMAND) failed");
                Json.Out(new JObj { ["cmdid"] = new JNum(id), ["path"] = new JStr(string.Join(">", segs)), ["hwnd"] = new JStr(h.ToString()) });
                return 0;
            }
            if (sub == IntPtr.Zero)
                throw MenuNotFound(segs, s, seg, matched, cands, "\"" + hitLabel + "\" is not a submenu (level " + s + ")");
            cur = sub;
        }
        throw new ToolError("invoke: unreachable path state");
    }

    static ToolError MenuNotFound(List<string> segs, int level, string seg, JArr matched, JArr cands, string msg)
    {
        var p = new JObj { ["error"] = new JStr(msg) };
        p["path"] = new JStr(string.Join(">", segs));
        p["level"] = new JNum(level);
        p["segment"] = new JStr(seg);
        p["matched"] = matched;
        p["candidates"] = cands;
        return new ToolError(p, "invoke: " + msg);
    }

    static string NormMenu(string s)
    {
        int t = s.IndexOf('\t');
        if (t >= 0) s = s.Substring(0, t);
        return s.Replace("&", "").Trim();
    }

    // ---------------- select ----------------

    static int Select(Args a)
    {
        using (Href hr = Href.Open(a))
        {
            IntPtr h = hr.H;
            string idxS = a.Opt("index");
            string text = a.Opt("text");
            if ((idxS == null) == (text == null))
                throw new UsageError("select: give exactly one of --index <n> / --text <substr>");
            string cls = W.ClassOf(h);
            string lower = cls.ToLowerInvariant();
            int pid = W.PidOf(h);

            if (lower.Contains("listbox"))
                return SelectSimpleList(h, cls, pid, idxS, text, "ListBox", Win32.LB_GETCOUNT, Win32.LB_GETTEXTLEN, Win32.LB_GETTEXT, Win32.LB_SETCURSEL);
            if (lower.Contains("combobox"))
                return SelectSimpleList(h, cls, pid, idxS, text, "ComboBox", Win32.CB_GETCOUNT, Win32.CB_GETLBTEXTLEN, Win32.CB_GETLBTEXT, Win32.CB_SETCURSEL);
            if (lower.Contains("systreeview32") || lower.Contains("treeview"))
                return SelectTree(h, cls, pid, idxS, text);
            if (lower.Contains("syslistview32") || lower.Contains("listview"))
                return SelectListview(h, cls, pid, idxS, text);
            throw new ToolError("unsupported control class \"" + cls + "\" for select (supported: *listbox*, *combobox*, SysTreeView32, SysListView32)");
        }
    }

    static int ParseIndex(string s, int count)
    {
        int v;
        if (s == null || !int.TryParse(s, out v)) throw new UsageError("--index: expected integer");
        if (v < 0 || v >= count) throw new ToolError("--index " + v + " out of range (count=" + count + ")");
        return v;
    }

    /// LB_GETTEXT / CB_GETLBTEXT with a LOCAL buffer: for cross-desktop SendMessage the system
    /// marshals these like WM_GETTEXT (buffer interpreted in the CALLER's address space), so the
    /// classic remote-buffer trick does NOT apply here - verified empirically.
    static string ReadListText(IntPtr h, int i, uint getText, int len)
    {
        int chars = Math.Min(len + 1, 1000);
        if (chars < 2) chars = 2;
        IntPtr buf = Marshal.AllocHGlobal(chars * 2);
        try
        {
            W.Smt(h, getText, (ulong)(uint)i, (long)buf);
            return Marshal.PtrToStringUni(buf) ?? "";
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// ListBox/ComboBox family: set selection by message, then post the change notification to the parent dialog.
    static int SelectSimpleList(IntPtr h, string cls, int pid, string idxS, string text,
        string family, uint getCount, uint getLen, uint getText, uint setSel)
    {
        long countL = W.L(W.Smt(h, getCount, 0, 0));
        if (countL <= 0) throw new ToolError(family + " has no items (count=" + countL + ")");
        int count = (int)countL;

        int index;
        string itemText = null;
        bool textAvail = true;
        {
            if (text != null)
            {
                index = -1;
                for (int i = 0; i < count; i++)
                {
                    int len = (int)W.L(W.Smt(h, getLen, (ulong)(uint)i, 0));
                    if (len <= 0) continue;
                    string s = ReadListText(h, i, getText, len);
                    if (s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) { index = i; itemText = s; break; }
                }
                if (index < 0)
                {
                    bool anyText = false;
                    for (int i = 0; i < count && !anyText; i++) anyText = W.L(W.Smt(h, getLen, (ulong)(uint)i, 0)) > 0;
                    if (!anyText)
                        throw new ToolError("owner-draw " + family + ": item strings are not readable via messages; use --index instead");
                    throw new ToolError("no " + family + " item contains \"" + text + "\" (count=" + count + ")");
                }
            }
            else
            {
                index = ParseIndex(idxS, count);
                int len = (int)W.L(W.Smt(h, getLen, (ulong)(uint)index, 0));
                if (len > 0) itemText = ReadListText(h, index, getText, len);
                else textAvail = false; // owner-draw item: selection by index still works
            }
        }

        long setR;
        bool ok = W.SmtR(h, setSel, (ulong)(uint)index, 0, out setR);
        if (!ok || setR == -1) throw new ToolError(family + " set-selection failed for index " + index);

        IntPtr parent = Win32.GetParent(h);
        if (parent == IntPtr.Zero) parent = h;
        int cid = Win32.GetDlgCtrlID(h) & 0xFFFF;
        // LBN_SELCHANGE == CBN_SELCHANGE == 1
        if (!W.Post(parent, Win32.WM_COMMAND, (1UL << 16) | (uint)cid, (long)h))
            throw ToolError.Win32("PostMessageW(WM_COMMAND notification) to parent failed");

        var o = new JObj
        {
            ["control"] = new JStr(family),
            ["class"] = new JStr(cls),
            ["index"] = new JNum(index),
            ["notified"] = new JBool(true)
        };
        if (textAvail) o["text"] = new JStr(itemText ?? "");
        else o["text_available"] = new JBool(false);
        Json.Out(o);
        return 0;
    }

    static int SelectTree(IntPtr h, string cls, int pid, string idxS, string text)
    {
        using (var xm = new XMem(pid))
        {
            IntPtr pStruct = xm.Alloc(4096);
            IntPtr pText = new IntPtr(pStruct.ToInt64() + 128);
            var items = new List<IntPtr>();
            IntPtr root = W.P(W.Smt(h, Win32.TVM_GETNEXTITEM, Win32.TVGN_ROOT, 0));
            if (root != IntPtr.Zero) CollectTree(h, root, items);
            if (items.Count == 0) throw new ToolError("TreeView has no items");

            int index;
            string itemText = null;
            if (text != null)
            {
                index = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    string s = TreeText(h, xm, pStruct, pText, items[i]);
                    if (s.Length > 0 && s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    { index = i; itemText = s; break; }
                }
                if (index < 0)
                    throw new ToolError("no TreeView item contains \"" + text + "\" (count=" + items.Count + ")");
            }
            else
            {
                index = ParseIndex(idxS, items.Count);
                itemText = TreeText(h, xm, pStruct, pText, items[index]);
            }

            long r;
            // TVGN_CARET: the control emits TVN_SELCHANGED itself; no extra notification needed
            bool ok = W.SmtR(h, Win32.TVM_SELECTITEM, Win32.TVGN_CARET, (long)items[index], out r);
            if (!ok || r == 0) throw new ToolError("TVM_SELECTITEM failed for index " + index);

            Json.Out(new JObj
            {
                ["control"] = new JStr("SysTreeView32"),
                ["class"] = new JStr(cls),
                ["index"] = new JNum(index),
                ["notified"] = new JBool(true),
                ["text"] = new JStr(itemText ?? "")
            });
        }
        return 0;
    }

    static void CollectTree(IntPtr h, IntPtr first, List<IntPtr> items)
    {
        IntPtr it = first;
        int guard = 0;
        while (it != IntPtr.Zero && guard++ < 65536)
        {
            items.Add(it);
            IntPtr child = W.P(W.Smt(h, Win32.TVM_GETNEXTITEM, Win32.TVGN_CHILD, (long)it));
            if (child != IntPtr.Zero) CollectTree(h, child, items);
            it = W.P(W.Smt(h, Win32.TVM_GETNEXTITEM, Win32.TVGN_NEXT, (long)it));
        }
    }

    static string TreeText(IntPtr h, XMem xm, IntPtr pStruct, IntPtr pText, IntPtr item)
    {
        var tvi = new TVITEMW
        {
            mask = Win32.TVIF_TEXT | Win32.TVIF_HANDLE,
            hItem = item,
            pszText = pText,
            cchTextMax = 512
        };
        xm.Write(pStruct, StructBytes(tvi));
        W.Smt(h, Win32.TVM_GETITEMW, 0, (long)pStruct);
        return xm.ReadWString(pText, 512);
    }

    static int SelectListview(IntPtr h, string cls, int pid, string idxS, string text)
    {
        long countL = W.L(W.Smt(h, Win32.LVM_GETITEMCOUNT, 0, 0));
        if (countL <= 0) throw new ToolError("ListView has no items (count=" + countL + ")");
        int count = (int)countL;

        using (var xm = new XMem(pid))
        {
            IntPtr pStruct = xm.Alloc(4096);
            IntPtr pText = new IntPtr(pStruct.ToInt64() + 256);

            int index;
            string itemText = null;
            if (text != null)
            {
                index = -1;
                for (int i = 0; i < count; i++)
                {
                    string s = LvText(h, xm, pStruct, pText, i);
                    if (s.Length > 0 && s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    { index = i; itemText = s; break; }
                }
                if (index < 0)
                    throw new ToolError("no ListView item contains \"" + text + "\" (count=" + count + ")");
            }
            else
            {
                index = ParseIndex(idxS, count);
                itemText = LvText(h, xm, pStruct, pText, index);
            }

            // clear existing selection first (state changes make the control emit LVN_ITEMCHANGED itself)
            LvWriteState(xm, pStruct, 0, Win32.LVIS_SELECTED, index);
            long r1;
            if (!W.SmtR(h, Win32.LVM_SETITEMSTATE, unchecked((ulong)-1), (long)pStruct, out r1))
                throw new ToolError("LVM_SETITEMSTATE (clear-all) timed out");
            LvWriteState(xm, pStruct, Win32.LVIS_SELECTED | Win32.LVIS_FOCUSED, Win32.LVIS_SELECTED | Win32.LVIS_FOCUSED, index);
            long r2;
            if (!W.SmtR(h, Win32.LVM_SETITEMSTATE, (ulong)(uint)index, (long)pStruct, out r2))
                throw new ToolError("LVM_SETITEMSTATE (select) timed out");

            Json.Out(new JObj
            {
                ["control"] = new JStr("SysListView32"),
                ["class"] = new JStr(cls),
                ["index"] = new JNum(index),
                ["notified"] = new JBool(true),
                ["text"] = new JStr(itemText ?? "")
            });
        }
        return 0;
    }

    static void LvWriteState(XMem xm, IntPtr pStruct, uint state, uint stateMask, int iItem)
    {
        var lvi = new LVITEMW
        {
            mask = Win32.LVIF_STATE,
            iItem = iItem,
            iSubItem = 0,
            state = state,
            stateMask = stateMask
        };
        xm.Write(pStruct, StructBytes(lvi));
    }

    static string LvText(IntPtr h, XMem xm, IntPtr pStruct, IntPtr pText, int i)
    {
        var lvi = new LVITEMW
        {
            mask = Win32.LVIF_TEXT,
            iItem = i,
            iSubItem = 0,
            pszText = pText,
            cchTextMax = 512
        };
        xm.Write(pStruct, StructBytes(lvi));
        long n = W.L(W.Smt(h, Win32.LVM_GETITEMTEXTW, (ulong)(uint)i, (long)pStruct));
        if (n <= 0) return "";
        return xm.ReadWString(pText, (int)Math.Min(n, 512));
    }

    // ---------------- items ----------------

    static int Items(Args a)
    {
        using (Href hr = Href.Open(a))
        {
            IntPtr h = hr.H;
            string cls = W.ClassOf(h);
            string lower = cls.ToLowerInvariant();
            int pid = W.PidOf(h);

            if (lower.Contains("listbox"))
                return ItemsSimple(h, cls, pid, "ListBox", Win32.LB_GETCOUNT, Win32.LB_GETTEXTLEN, Win32.LB_GETTEXT);
            if (lower.Contains("combobox"))
                return ItemsSimple(h, cls, pid, "ComboBox", Win32.CB_GETCOUNT, Win32.CB_GETLBTEXTLEN, Win32.CB_GETLBTEXT);
            if (lower.Contains("systreeview32") || lower.Contains("treeview"))
                return ItemsTree(h, cls, pid);
            if (lower.Contains("syslistview32") || lower.Contains("listview"))
                return ItemsLv(h, cls, pid);
            throw new ToolError("unsupported control class \"" + cls + "\" for items (supported: *listbox*, *combobox*, SysTreeView32, SysListView32)");
        }
    }

    static int ItemsSimple(IntPtr h, string cls, int pid, string family, uint getCount, uint getLen, uint getText)
    {
        long countL = W.L(W.Smt(h, getCount, 0, 0));
        int count = countL > 0 ? (int)countL : 0;
        var items = new JArr();
        bool anyText = false;
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                int len = (int)W.L(W.Smt(h, getLen, (ulong)(uint)i, 0));
                string s = "";
                if (len > 0) { s = ReadListText(h, i, getText, len); anyText = true; }
                items.Items.Add(new JObj { ["index"] = new JNum(i), ["text"] = new JStr(s) });
            }
        }

        var o = new JObj
        {
            ["control"] = new JStr(family),
            ["class"] = new JStr(cls),
            ["count"] = new JNum(count)
        };
        if (count > 0 && !anyText)
        {
            o["items"] = new JArr();
            o["note"] = new JStr("owner-draw control: no strings via messages");
        }
        else o["items"] = items;
        Json.Out(o);
        return 0;
    }

    static int ItemsTree(IntPtr h, string cls, int pid)
    {
        var items = new JArr();
        using (var xm = new XMem(pid))
        {
            IntPtr pStruct = xm.Alloc(4096);
            IntPtr pText = new IntPtr(pStruct.ToInt64() + 128);
            var handles = new List<IntPtr>();
            IntPtr root = W.P(W.Smt(h, Win32.TVM_GETNEXTITEM, Win32.TVGN_ROOT, 0));
            if (root != IntPtr.Zero) CollectTree(h, root, handles);
            for (int i = 0; i < handles.Count; i++)
                items.Items.Add(new JObj { ["index"] = new JNum(i), ["text"] = new JStr(TreeText(h, xm, pStruct, pText, handles[i])) });
            Json.Out(new JObj { ["control"] = new JStr("SysTreeView32"), ["class"] = new JStr(cls), ["count"] = new JNum(handles.Count), ["items"] = items });
        }
        return 0;
    }

    static int ItemsLv(IntPtr h, string cls, int pid)
    {
        long countL = W.L(W.Smt(h, Win32.LVM_GETITEMCOUNT, 0, 0));
        int count = countL > 0 ? (int)countL : 0;
        var items = new JArr();
        using (var xm = new XMem(pid))
        {
            IntPtr pStruct = xm.Alloc(4096);
            IntPtr pText = new IntPtr(pStruct.ToInt64() + 256);
            for (int i = 0; i < count; i++)
                items.Items.Add(new JObj { ["index"] = new JNum(i), ["text"] = new JStr(LvText(h, xm, pStruct, pText, i)) });
            Json.Out(new JObj { ["control"] = new JStr("SysListView32"), ["class"] = new JStr(cls), ["count"] = new JNum(count), ["items"] = items });
        }
        return 0;
    }

    static byte[] StructBytes<T>(T s) where T : struct
    {
        int sz = Marshal.SizeOf<T>();
        IntPtr p = Marshal.AllocHGlobal(sz);
        try
        {
            Marshal.StructureToPtr(s, p, false);
            var b = new byte[sz];
            Marshal.Copy(p, b, 0, sz);
            return b;
        }
        finally { Marshal.FreeHGlobal(p); }
    }
}
