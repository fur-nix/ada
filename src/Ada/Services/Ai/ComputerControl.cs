using System.Runtime.InteropServices;
using Ada.Core;
using Ada.Native;

namespace Ada.Services.Ai;

/// <summary>
/// Bilgisayarı fare/klavye ile kullanma ve ekran görüntüsü alma. Asistanın "gözleri ve elleri".
/// Koordinatlar sanal ekrana göre (çok monitör dahil). Gönderilen ekran görüntüsü modelin boyutuna küçültülür.
/// </summary>
public static unsafe class ComputerControl
{
    /// <summary>Modele en son gönderilen ekran görüntüsünün boyutu (koordinat eşlemesi için).</summary>
    public static int LastImgW = 1, LastImgH = 1;

    public static int VX => Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
    public static int VY => Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
    public static int VW => Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
    public static int VH => Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);

    /// <summary>Tüm ekranı yakalar, en fazla maxW genişliğe küçültür, (png, gönderilen genişlik, gerçek genişlik) döndürür.</summary>
    public static (byte[] png, int outW, int outH, int realW, int realH)? Capture(int maxW = 1400)
    {
        int w = VW, h = VH;
        if (w <= 0 || h <= 0) return null;
        var screen = Win32.GetDC(IntPtr.Zero);
        var dc = Win32.CreateCompatibleDC(screen);
        var bmi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dib = Win32.CreateDIBSection(dc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        var old = Win32.SelectObject(dc, dib);
        try
        {
            Win32.BitBlt(dc, 0, 0, w, h, screen, VX, VY, 0x00CC0020); // SRCCOPY
            // BGRA, satır satır ters değil (biHeight negatif) → doğrudan oku
            var src = (byte*)bits;
            float k = MathF.Min(1f, maxW / (float)w);
            int ow = Math.Max(1, (int)(w * k)), oh = Math.Max(1, (int)(h * k));
            var rgba = new byte[ow * oh * 4];
            for (int y = 0; y < oh; y++)
            {
                int sy = (int)(y / k);
                for (int x = 0; x < ow; x++)
                {
                    int sx = (int)(x / k);
                    byte* p = src + (sy * w + sx) * 4;
                    int o = (y * ow + x) * 4;
                    rgba[o] = p[2]; rgba[o + 1] = p[1]; rgba[o + 2] = p[0]; rgba[o + 3] = 255;
                }
            }
            return (Png.Encode(ow, oh, rgba), ow, oh, w, h);
        }
        catch (Exception e) { Log.Error("capture", e); return null; }
        finally { Win32.SelectObject(dc, old); Win32.DeleteObject(dib); Win32.DeleteDC(dc); Win32.ReleaseDC(IntPtr.Zero, screen); }
    }

    /// <summary>Modele gönderilen görüntüdeki koordinatı gerçek ekran koordinatına çevirir.</summary>
    public static (int x, int y) ToScreen(int imgX, int imgY, int imgW, int imgH)
    {
        float kx = VW / (float)imgW, ky = VH / (float)imgH;
        return (VX + (int)(imgX * kx), VY + (int)(imgY * ky));
    }

    static void MoveAbs(int x, int y)
    {
        // SendInput mutlak koordinat 0..65535 birincil ekran; çok ekranda SetCursorPos daha güvenilir
        Win32.SetCursorPos(x, y);
    }

    public static void Move(int x, int y) => MoveAbs(x, y);

    public static void Click(int x, int y, string button = "left", int count = 1)
    {
        MoveAbs(x, y);
        Thread.Sleep(40);
        (uint down, uint up) = button switch
        {
            "right" => (0x0008u, 0x0010u),
            "middle" => (0x0020u, 0x0040u),
            _ => (0x0002u, 0x0004u),
        };
        for (int i = 0; i < count; i++)
        {
            SendMouse(down); SendMouse(up);
            if (count > 1) Thread.Sleep(60);
        }
    }

    public static void Scroll(int amount)
    {
        var inp = new Win32.INPUT2 { type = 0, u = new Win32.INPUTUNION { mi = new Win32.MOUSEINPUT2 { dwFlags = 0x0800, mouseData = (uint)(amount * 120) } } }; // MOUSEEVENTF_WHEEL
        Win32.SendInput(1, &inp, sizeof(Win32.INPUT2));
    }

    static void SendMouse(uint flags)
    {
        var inp = new Win32.INPUT2 { type = 0, u = new Win32.INPUTUNION { mi = new Win32.MOUSEINPUT2 { dwFlags = flags } } };
        Win32.SendInput(1, &inp, sizeof(Win32.INPUT2));
    }

    public static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\n') { Key(0x0D); continue; } // Enter
            SendUnicode(ch);
            Thread.Sleep(4);
        }
    }

    static void SendUnicode(char ch)
    {
        var down = new Win32.INPUT2 { type = 1, u = new Win32.INPUTUNION { ki = new Win32.KEYBDINPUT2 { wScan = ch, dwFlags = 0x0004 } } }; // KEYEVENTF_UNICODE
        var up = new Win32.INPUT2 { type = 1, u = new Win32.INPUTUNION { ki = new Win32.KEYBDINPUT2 { wScan = ch, dwFlags = 0x0004 | 0x0002 } } };
        var arr = stackalloc Win32.INPUT2[2] { down, up };
        Win32.SendInput(2, arr, sizeof(Win32.INPUT2));
    }

    static readonly Dictionary<string, ushort> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B, ["space"] = 0x20,
        ["backspace"] = 0x08, ["delete"] = 0x2E, ["del"] = 0x2E, ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22, ["ctrl"] = 0x11, ["control"] = 0x11,
        ["alt"] = 0x12, ["shift"] = 0x10, ["win"] = 0x5B, ["windows"] = 0x5B,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73, ["f5"] = 0x74, ["f6"] = 0x75,
        ["f7"] = 0x76, ["f8"] = 0x77, ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
    };

    /// <summary>"ctrl+c", "win", "alt+tab", "enter" gibi kombinasyonlar.</summary>
    public static void Hotkey(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var vks = new List<ushort>();
        foreach (var p in parts)
        {
            if (Keys.TryGetValue(p, out var vk)) vks.Add(vk);
            else if (p.Length == 1) { short s = Win32.VkKeyScanW(p[0]); vks.Add((ushort)(s & 0xFF)); }
        }
        if (vks.Count == 0) return;
        foreach (var vk in vks) KeyDown(vk);
        Thread.Sleep(20);
        for (int i = vks.Count - 1; i >= 0; i--) KeyUp(vks[i]);
    }

    static void Key(ushort vk) { KeyDown(vk); KeyUp(vk); }
    static void KeyDown(ushort vk)
    {
        var inp = new Win32.INPUT2 { type = 1, u = new Win32.INPUTUNION { ki = new Win32.KEYBDINPUT2 { wVk = vk } } };
        Win32.SendInput(1, &inp, sizeof(Win32.INPUT2));
    }
    static void KeyUp(ushort vk)
    {
        var inp = new Win32.INPUT2 { type = 1, u = new Win32.INPUTUNION { ki = new Win32.KEYBDINPUT2 { wVk = vk, dwFlags = 0x0002 } } };
        Win32.SendInput(1, &inp, sizeof(Win32.INPUT2));
    }
}
