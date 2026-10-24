using System;
using System.IO;
using System.Runtime.InteropServices;

internal static partial class Cmds
{
    // ---------------- shot ----------------
    // PrintWindow(PW_RENDERFULLCONTENT) with flag=0 fallback; GetDIBits 32bpp top-down;
    // hand-written PNG encoder; blank detection by pixel sampling.

    static int Shot(Args a)
    {
        using (Href hr = Href.Open(a))
        {
        IntPtr h = hr.H;
        string outPath = a.Req("out");

        RECT rc;
        Win32.GetWindowRect(h, out rc);
        int w = rc.R - rc.L;
        int hgt = rc.B - rc.T;
        if (w <= 0 || hgt <= 0)
            throw new ToolError("window rect is empty (" + w + "x" + hgt + ")");

        IntPtr hdc = Win32.GetDC(h);
        if (hdc == IntPtr.Zero) throw ToolError.Win32("GetDC failed");
        bool ok = false;
        bool fallback = false;
        byte[] px = null;
        try
        {
            IntPtr mem = Win32.CreateCompatibleDC(hdc);
            IntPtr bmp = Win32.CreateCompatibleBitmap(hdc, w, hgt);
            IntPtr oldBmp = Win32.SelectObject(mem, bmp);
            try
            {
                ok = Win32.PrintWindow(h, mem, Win32.PW_RENDERFULLCONTENT);
                if (!ok) { ok = Win32.PrintWindow(h, mem, 0); fallback = true; }
                if (!ok) throw ToolError.Win32("PrintWindow failed (both flags 2 and 0)");

                Win32.SelectObject(mem, oldBmp); // deselect before GetDIBits
                var bmi = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -hgt, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = (uint)(w * hgt * 4)
                };
                px = new byte[w * hgt * 4];
                int lines = Win32.GetDIBits(mem, bmp, 0, (uint)hgt, px, ref bmi, Win32.DIB_RGB_COLORS);
                if (lines == 0) throw ToolError.Win32("GetDIBits failed");
            }
            finally
            {
                Win32.DeleteObject(bmp);
                Win32.DeleteDC(mem);
            }
        }
        finally { Win32.ReleaseDC(h, hdc); }

        byte[] png = Png.Encode(w, hgt, px);
        File.WriteAllBytes(outPath, png);
        bool blank = Png.IsBlank(w, hgt, px);

        var o = new JObj
        {
            ["w"] = new JNum(w),
            ["h"] = new JNum(hgt),
            ["blank"] = new JBool(blank),
            ["out"] = new JStr(outPath)
        };
        if (fallback) o["note"] = new JStr("PrintWindow used flag=0 fallback");
        if (blank) o["warning"] = new JStr("captured image appears blank (all black)");
        Json.Out(o);
        }
        return 0;
    }
}
