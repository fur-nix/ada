using System.Numerics;
using Ada.Core;
using Ada.Native;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Services;

/// <summary>Başlat menüsü kısayolları ve uygulama ikonu.</summary>
public static class Shell
{
    static string MenuDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Ada");

    /// <summary>"Ada" ve "Ada Ayarları" kısayollarını oluşturur / exe yolu değiştiyse günceller.</summary>
    public static void EnsureStartMenu()
    {
        var exe = Environment.ProcessPath;
        if (exe == null || exe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)) return; // geliştirme derlemesi
        try
        {
            Directory.CreateDirectory(MenuDir);
            Directory.CreateDirectory(Data.Store.Dir);
            var stamp = Path.Combine(Data.Store.Dir, "startmenu.txt");
            bool fresh = File.Exists(Path.Combine(MenuDir, "Ada.lnk")) && File.Exists(Path.Combine(MenuDir, "Ada Ayarları.lnk"))
                         && File.Exists(stamp) && File.ReadAllText(stamp) == exe;
            if (fresh) return;
            CreateLink(Path.Combine(MenuDir, "Ada.lnk"), exe, "", "Ada — Windows için dynamic island");
            CreateLink(Path.Combine(MenuDir, "Ada Ayarları.lnk"), exe, "--settings", "Ada'nın görünümünü ve davranışını ayarla");
            File.WriteAllText(stamp, exe);
        }
        catch (Exception e) { Log.Error("StartMenu", e); }
    }

    static void CreateLink(string lnk, string target, string args, string desc)
    {
        var clsid = new Guid("00021401-0000-0000-C000-000000000046");
        if (Win32.CoCreateInstance(clsid, IntPtr.Zero, 1, typeof(IShellLinkW).GUID, out var p) != 0) return;
        var link = ComUtil.Wrap<IShellLinkW>(p);
        if (link == null) return;
        link.SetPath(target);
        link.SetArguments(args);
        link.SetDescription(desc);
        link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
        link.SetIconLocation(target, 0);
        ((IPersistFile)(object)link).Save(lnk, 1);
    }

    /// <summary>Uygulama ikonunu çizer (koyu yuvarlak kare + parlayan ada).</summary>
    public static void MakeIcon(string path)
    {
        using var g = new Gfx();
        var images = new List<(int, byte[])>();
        foreach (int px in new[] { 256, 64, 48, 32, 24, 16 })
        {
            g.SetScale(px / 256f);
            g.Begin(px, px);
            var r = new Rect(8, 8, 240, 240);
            g.VGradientRound(r, 56, Theme.Rgb(0x2A2D3E), Theme.Rgb(0x0B0B10));
            g.StrokeRound(new Rect(9, 9, 238, 238), 55, Theme.Rgb(0xFFFFFF, 0.10f), 2);
            g.Glow(new Vector2(128, 120), 150, 90, Theme.Accent.WithA(0.45f), r, 56);
            var pill = new Rect(40, 96, 176, 56);
            g.Shadow(pill, 28, 14, 0.6f, 6);
            g.FillRound(pill, 28, Theme.Rgb(0x000000));
            g.StrokeRound(pill, 28, Theme.Rgb(0xFFFFFF, 0.14f), 2);
            g.FillCircle(new Vector2(72, 124), 9, Theme.Accent);
            for (int i = 0; i < 4; i++)
            {
                float h = new[] { 18f, 30f, 22f, 12f }[i];
                g.FillRound(new Rect(150 + i * 13, 124 - h / 2, 7, h), 3.5f, Theme.Rgb(0xF5F5F7));
            }
            g.End();
            images.Add((px, Png.Encode(px, px, g.ReadRgba(px, px))));
        }
        Png.WriteIco(path, images);
    }
}
