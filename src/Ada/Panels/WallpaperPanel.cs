using System.Numerics;
using Ada.Core;
using Ada.Data;
using Ada.Services;
using Vortice.DirectWrite;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>Duvar kağıdı: eğik kartlı kaydırmalı galeri; ortadakine tıkla → uygula.</summary>
public sealed class WallpaperPanel : Panel
{
    readonly WallpaperService _wp;
    Spring _pos = new(0, 200, 26);
    int _cur;
    string _current = "";

    public WallpaperPanel(Island i) : base(i) => _wp = new WallpaperService(i.G);

    public override string Name => "Duvar Kağıdı";
    public override string Glyph => Core.Glyph.Theme;

    public override void OnShow()
    {
        _wp.Scan();
        _current = WallpaperService.Current();
        int idx = _wp.Files.FindIndex(f => string.Equals(f, _current, StringComparison.OrdinalIgnoreCase));
        _cur = idx >= 0 ? idx : Math.Min(2, Math.Max(0, _wp.Files.Count - 1)); // galeri iki yana dolu başlasın
        _pos.Snap(_cur);
    }

    public override void OnHide() { base.OnHide(); _wp.Release(); }

    public override bool OnKey(int vk, bool ctrl)
    {
        if (vk == 0x25) { Move(-1); return true; }
        if (vk == 0x27) { Move(1); return true; }
        if (vk == 0x0D && _cur < _wp.Files.Count) { Apply(_wp.Files[_cur]); return true; }
        return base.OnKey(vk, ctrl);
    }

    void Move(int d)
    {
        if (_wp.Files.Count == 0) return;
        _cur = Math.Clamp(_cur + d, 0, _wp.Files.Count - 1);
        _pos.Target = _cur;
    }

    void Apply(string f)
    {
        WallpaperService.Apply(f);
        _current = f;
        I.Toast("Duvar kağıdı değişti", Path.GetFileNameWithoutExtension(f), Core.Glyph.Theme, Theme.Accent, 5);
    }

    public override void Draw(Rect a)
    {
        var files = _wp.Files;
        // üst
        G.FillRound(new Rect(a.X, a.Y, 92, 26), 13, Theme.Accent);
        G.Text("Klasörüm", new Rect(a.X, a.Y, 92, 26), 11.5f, Theme.TileOnText, FontWeight.Bold, TextAlignment.Center);
        G.Text($"{files.Count} resim · {_wp.Folder}", new Rect(a.X + 104, a.Y, a.Width - 260, 26), 11, Theme.Text3, FontWeight.SemiBold);
        if (U.Pill(Ui.Id("wp.folder"), new Rect(a.Right - 140, a.Y, 140, 26), "Klasör seç…", Theme.Surface, Theme.Text2, Core.Glyph.Folder))
            Dispatcher.Post(PickFolder);

        var stage = new Rect(a.X, a.Y + 36, a.Width, a.Height - 36);
        if (files.Count == 0)
        {
            Empty(stage, Core.Glyph.Theme, "Bu klasörde resim yok", "Klasör seç ile duvar kağıtlarının olduğu klasörü göster");
            return;
        }

        if (U.Hit(stage) && U.Wheel != 0) Move(U.Wheel < 0 ? 1 : -1);
        _pos.Step(U.Dt);
        if (!_pos.Settled) U.NeedsFrame = true;

        float cx = stage.CenterX, cy = stage.CenterY;
        float mainW = 330, mainH = stage.Height - 16;
        int budget = 2;
        // uzaktan yakına çiz (ortadaki en üstte)
        var order = Enumerable.Range(Math.Max(0, _cur - 4), Math.Min(files.Count, _cur + 5) - Math.Max(0, _cur - 4))
            .OrderByDescending(i => MathF.Abs(i - _pos.Value)).ToList();
        foreach (int i in order)
        {
            float d = i - _pos.Value;            // ortadan uzaklık (kesirli, animasyonlu)
            float ad = MathF.Abs(d);
            if (ad > 4) continue;
            float near = MathF.Max(0, 1 - ad);   // 1 = ortada
            float w = Ease.Lerp(74, mainW, near), h = Ease.Lerp(mainH * 0.82f, mainH, near);
            float side = MathF.Sign(d);
            float x = cx + side * (MathF.Min(ad, 1) * (mainW / 2 + 52) + MathF.Max(0, ad - 1) * 82) - w / 2;
            var r = new Rect(x, cy - h / 2, w, h);
            if (r.Right < stage.X - 40 || r.X > stage.Right + 40) continue;

            // eğiklik: yandakiler içe doğru bükülmüş görünür
            float skew = -side * 0.16f * MathF.Min(ad, 1);
            G.PushMatrix(Matrix3x2.CreateSkew(0, skew, new Vector2(r.CenterX, r.CenterY)));
            var bmp = _wp.Thumb(files[i], ref budget, out bool pending);
            if (pending) U.NeedsFrame = true;
            G.Shadow(r, 16, 10, 0.5f * (0.4f + near), 6);
            if (bmp != null) G.BitmapCover(bmp, r, 16, 0.55f + 0.45f * near);
            else G.FillRound(r, 16, Theme.SurfaceHi);
            bool isCurrent = string.Equals(files[i], _current, StringComparison.OrdinalIgnoreCase);
            if (isCurrent) G.StrokeRound(r, 16, Theme.Accent, 2.4f);
            else G.StrokeRound(r, 16, Theme.Rgb(0xFFFFFF, 0.10f + 0.15f * near), 1.2f);
            G.PopTransform();

            int id = Ui.Id("wp.card", i);
            if (U.Click(id, r))
            {
                if (i == _cur) Apply(files[i]);
                else { _cur = i; _pos.Target = i; }
            }
            if (near > 0.9f)
            {
                float hv = U.Anim(id, U.Hit(r) ? 1 : 0);
                if (hv > 0.01f || isCurrent)
                {
                    var tag = new Rect(r.X + 12, r.Bottom - 34, r.Width - 24, 24);
                    G.FillRound(tag, 12, Theme.Rgb(0x000000, 0.55f));
                    G.Text(isCurrent ? "✓ Şu anki duvar kağıdı" : "Tıkla: uygula", tag, 11, isCurrent ? Theme.Accent : Theme.Text, FontWeight.Bold, TextAlignment.Center);
                }
            }
        }
    }

    void PickFolder()
    {
        var f = AppsService.PickFile(I.Hwnd, "Duvar kağıdı klasöründen bir resim seç", "Resimler\0*.jpg;*.jpeg;*.png;*.bmp\0\0");
        if (f == null) return;
        Store.Current.WallpaperFolder = Path.GetDirectoryName(f);
        Store.Save();
        _wp.Scan(force: true);
        OnShow();
        I.Invalidate();
    }
}
