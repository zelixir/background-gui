using System;
using System.IO;
using System.IO.Compression;
using System.Text;

/// Hand-written PNG encoder (8-bit RGB, no System.Drawing, no external packages).
/// zlib frame: 0x78 0x9C + raw DeflateStream + adler32 (big-endian). CRC32 table-driven.
internal static class Png
{
    static readonly byte[] Sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// bgra: 32bpp BGRA pixel buffer from GetDIBits (top-down). Returns PNG file bytes.
    public static byte[] Encode(int w, int h, byte[] bgra)
    {
        int stride = w * 3;
        byte[] raw = new byte[h * (stride + 1)];
        for (int y = 0; y < h; y++)
        {
            int ro = y * (stride + 1);
            raw[ro] = 0; // filter type 0 (None) per scanline
            int src = y * w * 4;
            int di = ro + 1;
            for (int x = 0; x < w; x++)
            {
                int s = src + x * 4;
                raw[di++] = bgra[s + 2]; // R
                raw[di++] = bgra[s + 1]; // G
                raw[di++] = bgra[s];     // B
            }
        }

        using (var ms = new MemoryStream())
        {
            ms.Write(Sig, 0, 8);

            var ihdr = new byte[13];
            Be32(ihdr, 0, w);
            Be32(ihdr, 4, h);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 2;  // color type: truecolor RGB
            ihdr[10] = 0; // compression
            ihdr[11] = 0; // filter
            ihdr[12] = 0; // interlace
            Chunk(ms, "IHDR", ihdr);
            Chunk(ms, "IDAT", ZlibCompress(raw));
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        Be32(len, 0, data.Length);
        s.Write(len, 0, 4);
        byte[] tb = Encoding.ASCII.GetBytes(type);
        s.Write(tb, 0, 4);
        s.Write(data, 0, data.Length);
        var cb = new byte[4];
        Be32(cb, 0, unchecked((int)Crc32(tb, data)));
        s.Write(cb, 0, 4);
    }

    static void Be32(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    static byte[] ZlibCompress(byte[] raw)
    {
        using (var outMs = new MemoryStream())
        {
            outMs.WriteByte(0x78); // CMF: deflate, 32K window
            outMs.WriteByte(0x9C); // FLG: check bits
            using (var def = new DeflateStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
                def.Write(raw, 0, raw.Length);
            uint a = 1, b = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                a = (a + raw[i]) % 65521;
                b = (b + a) % 65521;
            }
            var t = new byte[4];
            Be32(t, 0, unchecked((int)((b << 16) | a)));
            outMs.Write(t, 0, 4);
            return outMs.ToArray();
        }
    }

    static uint[] _crcTable;

    static uint Crc32(byte[] type, byte[] data)
    {
        if (_crcTable == null)
        {
            _crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                _crcTable[n] = c;
            }
        }
        uint crc = 0xFFFFFFFFu;
        for (int i = 0; i < type.Length; i++) crc = _crcTable[(crc ^ type[i]) & 0xFF] ^ (crc >> 8);
        for (int i = 0; i < data.Length; i++) crc = _crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// Sample pixels; true when every sampled pixel is pure black (all channels 0).
    public static bool IsBlank(int w, int h, byte[] bgra)
    {
        long total = (long)w * h;
        long step = Math.Max(1, total / 4096);
        for (long p = 0; p < total; p += step)
        {
            int s = (int)(p * 4);
            if (bgra[s] != 0 || bgra[s + 1] != 0 || bgra[s + 2] != 0) return false;
        }
        return true;
    }
}
