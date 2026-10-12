using System.IO.Compression;

namespace Ada.Core;

/// <summary>Bağımlılıksız minik PNG yazıcı (RGBA8).</summary>
public static class Png
{
    static readonly uint[] Crc = MakeTable();

    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public static void Write(string path, int w, int h, byte[] rgba)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 6; // 8 bit, RGBA
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
        {
            for (int y = 0; y < h; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * w * 4, w * 4);
            }
        }
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint c = 0xFFFFFFFF;
        foreach (var b in t) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
        var crc = new byte[4]; Be(crc, 0, c ^ 0xFFFFFFFF);
        s.Write(crc);
    }

    static void Be(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
}
