using System.Drawing;
using System.Numerics;
using Ada.Native;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;
using D2DFactoryType = Vortice.Direct2D1.FactoryType;
using DWFactoryType = Vortice.DirectWrite.FactoryType;
using Rect = Vortice.Mathematics.Rect;
using Size = Vortice.Mathematics.Size;
using AlphaMode = Vortice.DCommon.AlphaMode;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace Ada.Core;

/// <summary>
/// Direct2D (yazılım) → bellek DIB → UpdateLayeredWindow. Tüm çizimler DIP cinsinden.
/// </summary>
public sealed class Gfx : IDisposable
{
    public readonly ID2D1Factory1 D2;
    public readonly IDWriteFactory DW;
    public readonly IWICImagingFactory Wic;
    readonly ID2D1DCRenderTarget _rt;
    readonly ID2D1SolidColorBrush _brush;
    readonly ID2D1StrokeStyle _roundCap;
    IntPtr _memDc, _dib, _oldBmp, _bits;
    int _dibW, _dibH;

    public float Scale { get; private set; } = 1f;
    /// <summary>Global opaklık çarpanı (içerik fade'leri için).</summary>
    public float Alpha = 1f;
    public ID2D1RenderTarget Rt => _rt;

    readonly Dictionary<(string, float, FontWeight, TextAlignment, ParagraphAlignment, bool), IDWriteTextFormat> _formats = new();
    readonly Dictionary<(string, float, FontWeight, string), float> _measure = new();
    readonly Stack<Matrix3x2> _xf = new();

    public Gfx()
    {
        D2 = D2D1.D2D1CreateFactory<ID2D1Factory1>(D2DFactoryType.SingleThreaded);
        DW = DWrite.DWriteCreateFactory<IDWriteFactory>(DWFactoryType.Shared);
        Wic = new IWICImagingFactory();
        var props = new RenderTargetProperties(RenderTargetType.Software,
            new PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm,AlphaMode.Premultiplied), 96, 96, RenderTargetUsage.None, FeatureLevel.Default);
        _rt = D2.CreateDCRenderTarget(props);
        _rt.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        _rt.AntialiasMode = AntialiasMode.PerPrimitive;
        _brush = _rt.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _roundCap = D2.CreateStrokeStyle(new StrokeStyleProperties1
        {
            StartCap = CapStyle.Round, EndCap = CapStyle.Round, DashCap = CapStyle.Round, LineJoin = LineJoin.Round
        });
    }

    public void SetScale(float s) { Scale = s; _rt.SetDpi(96 * s, 96 * s); }

    void EnsureSurface(int w, int h)
    {
        if (w <= _dibW && h <= _dibH && _memDc != IntPtr.Zero) return;
        w = Math.Max(w, _dibW); h = Math.Max(h, _dibH);
        FreeSurface();
        var screen = Win32.GetDC(IntPtr.Zero);
        _memDc = Win32.CreateCompatibleDC(screen);
        Win32.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32
        };
        _dib = Win32.CreateDIBSection(_memDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        _oldBmp = Win32.SelectObject(_memDc, _dib);
        _dibW = w; _dibH = h;
    }

    void FreeSurface()
    {
        if (_memDc == IntPtr.Zero) return;
        Win32.SelectObject(_memDc, _oldBmp);
        Win32.DeleteObject(_dib);
        Win32.DeleteDC(_memDc);
        _memDc = IntPtr.Zero;
    }

    /// <summary>
    /// Çizim sürüyor mu? Bazı COM çağrıları (ikon çıkarma vb.) beklerken mesaj pompalar; o sırada gelen mesajların
    /// ikinci bir çizime başlamaması gerekir (D2DERR_WRONG_STATE çökmesi). Island bu bayrağa bakıp mesajları erteler.
    /// </summary>
    public bool InDraw { get; private set; }

    public void Begin(int pw, int ph)
    {
        if (InDraw) throw new InvalidOperationException("İç içe çizim");
        InDraw = true;
        EnsureSurface(pw, ph);
        _rt.BindDC(_memDc, new RawRect(0, 0, pw, ph));
        _rt.BeginDraw();
        _rt.Transform = Matrix3x2.Identity;
        _rt.Clear(new Color4(0, 0, 0, 0));
        Alpha = 1f;
    }

    public void End()
    {
        InDraw = false;
        var r = _rt.EndDraw();
        if (r.Failure) Log.Info("EndDraw failed: " + r.Code);
    }

    public void Present(IntPtr hwnd, int x, int y, int pw, int ph)
    {
        var dst = new POINT(x, y);
        var size = new SIZE(pw, ph);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        Win32.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA);
    }

    /// <summary>Pencereyi taşımadan içeriği günceller (kullanıcı sürüklerken konum sistemde kalır).</summary>
    public void PresentInPlace(IntPtr hwnd, int pw, int ph)
    {
        var size = new SIZE(pw, ph);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        Win32.UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, ref size, _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA);
    }

    // ---------- transform ----------
    public void PushTranslate(float x, float y, float scale = 1f, Vector2 origin = default)
    {
        _xf.Push(_rt.Transform);
        var m = Matrix3x2.CreateScale(scale, origin) * Matrix3x2.CreateTranslation(x, y) * _rt.Transform;
        _rt.Transform = m;
    }
    public void PopTransform() => _rt.Transform = _xf.Pop();

    public void PushClip(Rect r) => _rt.PushAxisAlignedClip(new RawRectF(r.Left, r.Top, r.Right, r.Bottom), AntialiasMode.Aliased);
    public void PopClip() => _rt.PopAxisAlignedClip();

    // ---------- brushes ----------
    ID2D1SolidColorBrush B(Color4 c)
    {
        _brush.Color = new Color4(c.R, c.G, c.B, c.A * Alpha);
        return _brush;
    }

    // Gradyan fırçaları önbelleği: her karede COM nesnesi yaratmak yerine renk çiftine göre yeniden kullan.
    // Tasarım alfaları renklerde kalır (anahtara girer, 6 bit kuantize); global Alpha fırça opaklığı olarak uygulanır.
    readonly Dictionary<long, ID2D1LinearGradientBrush> _linBrushes = new();
    readonly Dictionary<long, ID2D1RadialGradientBrush> _radBrushes = new();

    static long Key(Color4 a, Color4 b)
    {
        static long Q(Color4 c) =>
            ((long)(Math.Clamp(c.R, 0, 1) * 31) << 15) | ((long)(Math.Clamp(c.G, 0, 1) * 31) << 10) |
            ((long)(Math.Clamp(c.B, 0, 1) * 31) << 5) | (long)(Math.Clamp(c.A, 0, 1) * 31);
        return (Q(a) << 20) | Q(b);
    }

    ID2D1LinearGradientBrush Linear(Color4 c0, Color4 c1)
    {
        var key = Key(c0, c1);
        if (!_linBrushes.TryGetValue(key, out var br))
        {
            if (_linBrushes.Count > 400) { foreach (var v in _linBrushes.Values) v.Dispose(); _linBrushes.Clear(); }
            using var stops = _rt.CreateGradientStopCollection(new[] { new GradientStop(0, c0), new GradientStop(1, c1) });
            br = _rt.CreateLinearGradientBrush(new LinearGradientBrushProperties(Vector2.Zero, new Vector2(0, 1)), stops);
            _linBrushes[key] = br;
        }
        br.Opacity = Alpha;
        return br;
    }

    ID2D1RadialGradientBrush Radial(Color4 c0, Color4 c1)
    {
        var key = Key(c0, c1);
        if (!_radBrushes.TryGetValue(key, out var br))
        {
            if (_radBrushes.Count > 200) { foreach (var v in _radBrushes.Values) v.Dispose(); _radBrushes.Clear(); }
            using var stops = _rt.CreateGradientStopCollection(new[] { new GradientStop(0, c0), new GradientStop(1, c1) });
            br = _rt.CreateRadialGradientBrush(new RadialGradientBrushProperties { Center = Vector2.Zero, RadiusX = 1, RadiusY = 1 }, stops);
            _radBrushes[key] = br;
        }
        br.Opacity = Alpha;
        return br;
    }

    static RectangleF RF(Rect r) => new(r.X, r.Y, r.Width, r.Height);

    // ---------- shapes ----------
    public void FillRect(Rect r, Color4 c) => _rt.FillRectangle(new RawRectF(r.Left, r.Top, r.Right, r.Bottom), B(c));
    public void FillRound(Rect r, float rad, Color4 c)
    {
        rad = MathF.Min(rad, MathF.Min(r.Width, r.Height) / 2);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(r), rad, rad), B(c));
    }
    public void StrokeRound(Rect r, float rad, Color4 c, float w = 1f)
    {
        rad = MathF.Min(rad, MathF.Min(r.Width, r.Height) / 2);
        _rt.DrawRoundedRectangle(new RoundedRectangle(RF(r), rad, rad), B(c), w);
    }
    public void FillCircle(Vector2 c, float r, Color4 col) => _rt.FillEllipse(new Ellipse(c, r, r), B(col));
    public void StrokeCircle(Vector2 c, float r, Color4 col, float w) => _rt.DrawEllipse(new Ellipse(c, r, r), B(col), w, _roundCap);
    public void Line(Vector2 a, Vector2 b, Color4 c, float w = 1f) => _rt.DrawLine(a, b, B(c), w, _roundCap);

    /// <summary>Saat yönünde yay; açı 0 = tepe.</summary>
    public void Arc(Vector2 center, float radius, float startDeg, float sweepDeg, Color4 c, float width)
    {
        if (sweepDeg <= 0.1f) return;
        if (sweepDeg >= 359.9f) { StrokeCircle(center, radius, c, width); return; }
        static Vector2 P(Vector2 ctr, float r, float deg)
        {
            var a = (deg - 90) * MathF.PI / 180f;
            return new Vector2(ctr.X + r * MathF.Cos(a), ctr.Y + r * MathF.Sin(a));
        }
        using var geo = D2.CreatePathGeometry();
        using (var sink = geo.Open())
        {
            sink.BeginFigure(P(center, radius, startDeg), FigureBegin.Hollow);
            sink.AddArc(new ArcSegment(P(center, radius, startDeg + sweepDeg), new Size(radius, radius), 0,
                SweepDirection.Clockwise, sweepDeg > 180 ? ArcSize.Large : ArcSize.Small));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
        _rt.DrawGeometry(geo, B(c), width, _roundCap);
    }

    /// <summary>
    /// Çentik "kulağı": gövdenin yan kenarıyla ekranın üst kenarını birleştiren içbükey kıvrım.
    /// corner = gövde kenarının y=0'daki noktası.
    /// </summary>
    public void NotchEar(Vector2 corner, float r, bool left, Color4 c)
    {
        if (r < 0.5f) return;
        using var geo = D2.CreatePathGeometry();
        using (var s = geo.Open())
        {
            float dir = left ? -1 : 1;
            s.BeginFigure(new Vector2(corner.X + dir * r, corner.Y), FigureBegin.Filled);
            s.AddArc(new ArcSegment(new Vector2(corner.X, corner.Y + r), new Size(r, r), 0,
                left ? SweepDirection.Clockwise : SweepDirection.CounterClockwise, ArcSize.Small));
            s.AddLine(corner);
            s.EndFigure(FigureEnd.Closed);
            s.Close();
        }
        _rt.FillGeometry(geo, B(c));
    }

    /// <summary>Dalgalı çizgi (Android 13 tarzı müzik ilerlemesi).</summary>
    public void Wave(float x0, float x1, float y, float amp, float wavelength, float phase, Color4 c, float width)
    {
        if (x1 - x0 < 2) return;
        using var geo = D2.CreatePathGeometry();
        using (var s = geo.Open())
        {
            s.BeginFigure(new Vector2(x0, y + amp * MathF.Sin(phase)), FigureBegin.Hollow);
            for (float x = x0 + 2; x <= x1; x += 2)
                s.AddLine(new Vector2(x, y + amp * MathF.Sin(phase + (x - x0) / wavelength * MathF.Tau)));
            s.EndFigure(FigureEnd.Open);
            s.Close();
        }
        _rt.DrawGeometry(geo, B(c), width, _roundCap);
    }

    /// <summary>Çizgi grafik + altında gradyan dolgu.</summary>
    public void Sparkline(Rect r, ReadOnlySpan<float> values, float max, Color4 c)
    {
        int n = values.Length;
        if (n < 2 || max <= 0) return;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float v = Math.Clamp(values[i] / max, 0, 1);
            pts[i] = new Vector2(r.Left + r.Width * i / (n - 1), r.Bottom - v * r.Height);
        }
        using (var fill = D2.CreatePathGeometry())
        {
            using (var s = fill.Open())
            {
                s.BeginFigure(new Vector2(r.Left, r.Bottom), FigureBegin.Filled);
                foreach (var p in pts) s.AddLine(p);
                s.AddLine(new Vector2(r.Right, r.Bottom));
                s.EndFigure(FigureEnd.Closed);
                s.Close();
            }
            var lg = Linear(c.WithA(0.32f), c.WithA(0f));
            lg.StartPoint = new Vector2(0, r.Top); lg.EndPoint = new Vector2(0, r.Bottom);
            _rt.FillGeometry(fill, lg);
        }
        using var line = D2.CreatePathGeometry();
        using (var s = line.Open())
        {
            s.BeginFigure(pts[0], FigureBegin.Hollow);
            for (int i = 1; i < n; i++) s.AddLine(pts[i]);
            s.EndFigure(FigureEnd.Open);
            s.Close();
        }
        _rt.DrawGeometry(line, B(c), 1.6f, _roundCap);
    }

    public void VGradientRound(Rect r, float rad, Color4 top, Color4 bottom)
    {
        var lg = Linear(top, bottom);
        lg.StartPoint = new Vector2(0, r.Top); lg.EndPoint = new Vector2(0, r.Bottom);
        rad = MathF.Min(rad, MathF.Min(r.Width, r.Height) / 2);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(r), rad, rad), lg);
    }

    public void HGradientRound(Rect r, float rad, Color4 left, Color4 right)
    {
        var lg = Linear(left, right);
        lg.StartPoint = new Vector2(r.Left, 0); lg.EndPoint = new Vector2(r.Right, 0);
        rad = MathF.Min(rad, MathF.Min(r.Width, r.Height) / 2);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(r), rad, rad), lg);
    }

    /// <summary>Yumuşak radyal ışıma (renk → şeffaf).</summary>
    public void Glow(Vector2 center, float rx, float ry, Color4 c, Rect clip, float clipRadius)
    {
        var rg = Radial(c, c.WithA(0));
        rg.Center = center; rg.RadiusX = rx; rg.RadiusY = ry;
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(clip), clipRadius, clipRadius), rg);
    }

    /// <summary>Katmanlı yumuşak gölge.</summary>
    public void Shadow(Rect r, float rad, float spread, float strength, float dy = 4)
    {
        // Eşit alfalı, dışa doğru seyrekleşen katmanlar → üst üste binince yumuşak (yaklaşık gauss) düşüş.
        // 22 → 13 katman: yazılım render'ında kare başına ciddi tasarruf, görsel fark neredeyse yok.
        const int N = 13;
        float per = strength / N * 1.25f;
        for (int i = N; i >= 1; i--)
        {
            float t = MathF.Pow(i / (float)N, 1.7f);
            var rr = new Rect(r.X - spread * t, r.Y - spread * t + dy * t, r.Width + 2 * spread * t, r.Height + 2 * spread * t);
            FillRound(rr, rad + spread * t, new Color4(0, 0, 0, per));
        }
    }

    // ---------- bitmaps ----------
    public void Bitmap(ID2D1Bitmap bmp, Rect dest, float opacity = 1f) =>
        _rt.DrawBitmap(bmp, dest, opacity * Alpha, Vortice.Direct2D1.BitmapInterpolationMode.Linear, null);

    public void BitmapRound(ID2D1Bitmap bmp, Rect dest, float rad, float opacity = 1f)
    {
        var sz = bmp.Size;
        using var brush = _rt.CreateBitmapBrush(bmp, new BitmapBrushProperties
        {
            ExtendModeX = ExtendMode.Clamp, ExtendModeY = ExtendMode.Clamp,
            InterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode.Linear
        }, new BrushProperties { Opacity = opacity * Alpha, Transform = Matrix3x2.Identity });
        brush.Transform = Matrix3x2.CreateScale(dest.Width / sz.Width, dest.Height / sz.Height) * Matrix3x2.CreateTranslation(dest.X, dest.Y);
        rad = MathF.Min(rad, MathF.Min(dest.Width, dest.Height) / 2);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(dest), rad, rad), brush);
    }

    /// <summary>Görüntüyü oranını bozmadan alanı dolduracak şekilde kırpar (CSS object-fit: cover).</summary>
    public void BitmapCover(ID2D1Bitmap bmp, Rect dest, float rad, float opacity = 1f)
    {
        var sz = bmp.Size;
        float k = MathF.Max(dest.Width / sz.Width, dest.Height / sz.Height);
        float ox = dest.X + (dest.Width - sz.Width * k) / 2, oy = dest.Y + (dest.Height - sz.Height * k) / 2;
        using var brush = _rt.CreateBitmapBrush(bmp, new BitmapBrushProperties
        {
            ExtendModeX = ExtendMode.Clamp, ExtendModeY = ExtendMode.Clamp,
            InterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode.Linear
        }, new BrushProperties { Opacity = opacity * Alpha, Transform = Matrix3x2.Identity });
        brush.Transform = Matrix3x2.CreateScale(k) * Matrix3x2.CreateTranslation(ox, oy);
        rad = MathF.Min(rad, MathF.Min(dest.Width, dest.Height) / 2);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(dest), rad, rad), brush);
    }

    /// <summary>Yuvarlak dikdörtgeni bitmap fırçasıyla doldurur (fırça dönüşümü ekran ↔ pencere eşlemesi içindir).</summary>
    public void FillRoundBitmap(Rect r, float rad, ID2D1Bitmap bmp, Matrix3x2 brushTransform, float opacity = 1f)
    {
        using var brush = _rt.CreateBitmapBrush(bmp, new BitmapBrushProperties
        {
            ExtendModeX = ExtendMode.Clamp, ExtendModeY = ExtendMode.Clamp,
            InterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode.Linear
        }, new BrushProperties { Opacity = opacity * Alpha, Transform = Matrix3x2.Identity });
        brush.Transform = brushTransform;
        rad = MathF.Min(rad, MathF.Min(r.Width, r.Height) / 2);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(r), rad, rad), brush);
    }

    /// <summary>Tek radyal elips gölge (ikon altı) — katmanlı gölgeden çok daha ucuz.</summary>
    public void SoftShadow(Vector2 center, float rx, float ry, float alpha)
    {
        var rg = Radial(new Color4(0, 0, 0, alpha), new Color4(0, 0, 0, 0));
        rg.Center = center; rg.RadiusX = rx; rg.RadiusY = ry;
        _rt.FillEllipse(new Ellipse(center, rx, ry), rg);
    }

    /// <summary>
    /// Duvar kağıdının ekrana "doldur" yerleşimiyle küçültülmüş + kutu-bulanıklaştırılmış kopyası (buzlu cam için).
    /// </summary>
    public unsafe ID2D1Bitmap? BlurredWallpaper(string file, int monW, int monH, int downscale = 10)
    {
        try
        {
            using var dec = Wic.CreateDecoderFromFileName(file, System.IO.FileAccess.Read, DecodeOptions.CacheOnDemand);
            using var frame = dec.GetFrame(0);
            using var conv = Wic.CreateFormatConverter();
            conv.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapDitherType.None, null, 0, BitmapPaletteType.Custom);
            var sz = conv.Size;
            float k = MathF.Max(monW / (float)sz.Width, monH / (float)sz.Height);
            uint cw = (uint)Math.Min(sz.Width, monW / k), ch = (uint)Math.Min(sz.Height, monH / k);
            uint cx = (uint)((sz.Width - cw) / 2), cy = (uint)((sz.Height - ch) / 2);
            using var crop = Wic.CreateBitmapFromSourceRect(conv, cx, cy, cw, ch);
            int w = Math.Max(8, monW / downscale), h = Math.Max(8, monH / downscale);
            using var scaler = Wic.CreateBitmapScaler();
            scaler.Initialize(crop, (uint)w, (uint)h, Vortice.WIC.BitmapInterpolationMode.Fant);
            var buf = new byte[w * h * 4];
            scaler.CopyPixels((uint)(w * 4), buf);
            for (int pass = 0; pass < 3; pass++) { BoxBlur(buf, w, h, 3, true); BoxBlur(buf, w, h, 3, false); }
            using var wb = Wic.CreateBitmapFromMemory((uint)w, (uint)h, Vortice.WIC.PixelFormat.Format32bppPBGRA, buf, (uint)(w * 4));
            return _rt.CreateBitmapFromWicBitmap(wb, null);
        }
        catch (Exception e) { Log.Error("BlurredWallpaper", e); return null; }
    }

    static void BoxBlur(byte[] p, int w, int h, int r, bool horizontal)
    {
        int len = horizontal ? w : h, lines = horizontal ? h : w;
        var tmp = new int[len * 4];
        for (int line = 0; line < lines; line++)
        {
            int Idx(int i) => horizontal ? (line * w + i) * 4 : (i * w + line) * 4;
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int i = -r; i <= r; i++) sum += p[Idx(Math.Clamp(i, 0, len - 1)) + c];
                for (int i = 0; i < len; i++)
                {
                    tmp[i * 4 + c] = sum / (2 * r + 1);
                    sum += p[Idx(Math.Min(len - 1, i + r + 1)) + c] - p[Idx(Math.Max(0, i - r)) + c];
                }
            }
            for (int i = 0; i < len; i++)
                for (int c = 0; c < 4; c++) p[Idx(i) + c] = (byte)tmp[i * 4 + c];
        }
    }

    /// <summary>Pencerenin anlık görüntüsü (Dock önizlemesi). Simge durumundaki pencerelerde null.</summary>
    public unsafe ID2D1Bitmap? CaptureWindow(IntPtr hwnd, int maxW)
    {
        if (Win32.IsIconic(hwnd) || !Win32.GetWindowRect(hwnd, out var rc)) return null;
        int w = rc.Width, h = rc.Height;
        if (w < 8 || h < 8 || w > 8000 || h > 8000) return null;
        var screen = Win32.GetDC(IntPtr.Zero);
        var dc = Win32.CreateCompatibleDC(screen);
        Win32.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var dib = Win32.CreateDIBSection(dc, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        var old = Win32.SelectObject(dc, dib);
        try
        {
            if (!Win32.PrintWindow(hwnd, dc, 2)) return null; // PW_RENDERFULLCONTENT
            using var wb = Wic.CreateBitmapFromMemory((uint)w, (uint)h, Vortice.WIC.PixelFormat.Format32bppBGR, (uint)(w * 4), (uint)(w * h * 4), bits);
            float k = MathF.Min(1, maxW / (float)w);
            using var scaler = Wic.CreateBitmapScaler();
            scaler.Initialize(wb, (uint)Math.Max(1, w * k), (uint)Math.Max(1, h * k), Vortice.WIC.BitmapInterpolationMode.Fant);
            using var conv = Wic.CreateFormatConverter();
            conv.Initialize(scaler, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapDitherType.None, null, 0, BitmapPaletteType.Custom);
            return _rt.CreateBitmapFromWicBitmap(conv, null);
        }
        catch (Exception e) { Log.Error("CaptureWindow", e); return null; }
        finally
        {
            Win32.SelectObject(dc, old);
            Win32.DeleteObject(dib);
            Win32.DeleteDC(dc);
        }
    }

    public void PushMatrix(Matrix3x2 m) { _xf.Push(_rt.Transform); _rt.Transform = m * _rt.Transform; }

    /// <summary>Kodlanmış görüntüyü (png/jpg) çözer, max boyuta küçültür, baskın rengi hesaplar.</summary>
    public ID2D1Bitmap? LoadImage(byte[] data, int maxPx, out Color4 dominant)
    {
        dominant = Theme.Accent;
        try
        {
            using var ms = new MemoryStream(data);
            using var dec = Wic.CreateDecoderFromStream(ms, DecodeOptions.CacheOnDemand);
            using var frame = dec.GetFrame(0);
            return FromSource(frame, maxPx, out dominant);
        }
        catch (Exception e) { Log.Error("LoadImage", e); return null; }
    }

    /// <summary>
    /// Kabuk ikonunu (HBITMAP) yüksek kaliteyle bitmap'e çevirir: pikselleri üstten-alta okur, alfanın ön-çarpımlı
    /// olup olmadığını piksellerden tespit eder (yanlış varsayım = tırtıklı/parlak kenar), Fant ile hedef boyuta küçültür.
    /// </summary>
    public unsafe ID2D1Bitmap? FromHBitmap(IntPtr hbm, int maxPx)
    {
        try
        {
            if (Win32.GetObjectW(hbm, sizeof(BITMAP), out var bm) == 0 || bm.bmWidth <= 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            var bmi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var buf = new byte[w * h * 4];
            var dc = Win32.GetDC(IntPtr.Zero);
            int lines;
            fixed (byte* p = buf) lines = Win32.GetDIBits(dc, hbm, 0, (uint)h, p, ref bmi, 0);
            Win32.ReleaseDC(IntPtr.Zero, dc);
            if (lines == 0) return null;

            bool anyAlpha = false, straight = false;
            for (int i = 0; i < buf.Length; i += 4)
            {
                byte a = buf[i + 3];
                if (a != 0) anyAlpha = true;
                if (buf[i] > a || buf[i + 1] > a || buf[i + 2] > a) straight = true;
            }
            if (!anyAlpha) { for (int i = 3; i < buf.Length; i += 4) buf[i] = 255; straight = false; } // alfasız eski ikon
            var fmt = straight ? Vortice.WIC.PixelFormat.Format32bppBGRA : Vortice.WIC.PixelFormat.Format32bppPBGRA;
            using var src = Wic.CreateBitmapFromMemory((uint)w, (uint)h, fmt, buf, (uint)(w * 4));

            // 256 px ikonu olmayan uygulamalarda kabuk küçük ikonu büyük tuvalin köşesine koyar → dolu alanı kırp
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (buf[(y * w + x) * 4 + 3] > 8) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxX >= 0 && maxX - minX + 1 < w * 0.45f && maxY - minY + 1 < h * 0.45f)
            {
                int side = Math.Max(maxX - minX + 1, maxY - minY + 1);
                int cx = Math.Clamp((minX + maxX + 1) / 2 - side / 2, 0, w - side), cy = Math.Clamp((minY + maxY + 1) / 2 - side / 2, 0, h - side);
                using var crop = Wic.CreateBitmapFromSourceRect(src, (uint)cx, (uint)cy, (uint)side, (uint)side);
                return FromSource(crop, maxPx, out _);
            }
            return FromSource(src, maxPx, out _);
        }
        catch (Exception e) { Log.Error("FromHBitmap", e); return null; }
    }

    ID2D1Bitmap FromSource(IWICBitmapSource src, int maxPx, out Color4 dominant)
    {
        var size = src.Size;
        float k = MathF.Min(1f, maxPx / (float)Math.Max(size.Width, size.Height));
        uint w = (uint)Math.Max(1, size.Width * k), h = (uint)Math.Max(1, size.Height * k);
        // önce ön-çarpımlı biçime çevir, SONRA küçült: saydam piksellerin rengi kenara sızmaz (koyu hale yok)
        using var conv = Wic.CreateFormatConverter();
        conv.Initialize(src, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapDitherType.None, null, 0, BitmapPaletteType.Custom);
        using var scaler = Wic.CreateBitmapScaler();
        scaler.Initialize(conv, w, h, Vortice.WIC.BitmapInterpolationMode.Fant);
        dominant = Dominant(scaler, (int)w, (int)h);
        return _rt.CreateBitmapFromWicBitmap(scaler, null);
    }

    static Color4 Dominant(IWICBitmapSource src, int w, int h)
    {
        var buf = new byte[w * h * 4];
        src.CopyPixels((uint)(w * 4), buf);
        double r = 0, g = 0, b = 0, wsum = 0;
        for (int i = 0; i < buf.Length; i += 16)
        {
            float bb = buf[i] / 255f, gg = buf[i + 1] / 255f, rr = buf[i + 2] / 255f;
            float mx = MathF.Max(rr, MathF.Max(gg, bb)), mn = MathF.Min(rr, MathF.Min(gg, bb));
            float sat = mx - mn;
            double wt = 0.05 + sat * sat * 4 * mx; // canlı renkleri öne çıkar
            r += rr * wt; g += gg * wt; b += bb * wt; wsum += wt;
        }
        if (wsum <= 0) return Theme.Accent;
        var c = new Color4((float)(r / wsum), (float)(g / wsum), (float)(b / wsum), 1);
        // çok karanlık/soluk renkleri canlandır
        var hsv = c.ToHSV();
        var col = Color4.FromHSV(hsv.X, MathF.Max(hsv.Y, 0.45f), MathF.Max(hsv.Z, 0.85f), 1);
        return new Color4(col);
    }

    // ---------- text ----------
    public IDWriteTextFormat Format(float size, FontWeight w = FontWeight.Normal, TextAlignment ta = TextAlignment.Leading,
        ParagraphAlignment pa = ParagraphAlignment.Center, string font = Theme.Font, bool wrap = false)
    {
        var key = (font, size, w, ta, pa, wrap);
        if (_formats.TryGetValue(key, out var f)) return f;
        f = DW.CreateTextFormat(font, null!, w, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size, "tr-tr");
        f.TextAlignment = ta;
        f.ParagraphAlignment = pa;
        f.WordWrapping = wrap ? WordWrapping.Wrap : WordWrapping.NoWrap;
        if (!wrap)
        {
            using var sign = DW.CreateEllipsisTrimmingSign(f);
            f.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, sign);
        }
        _formats[key] = f;
        return f;
    }

    public void Text(string s, Rect r, float size, Color4 c, FontWeight w = FontWeight.Normal,
        TextAlignment ta = TextAlignment.Leading, ParagraphAlignment pa = ParagraphAlignment.Center, string font = Theme.Font, bool wrap = false)
    {
        if (string.IsNullOrEmpty(s) || r.Width <= 0) return;
        _rt.DrawText(s, Format(size, w, ta, pa, font, wrap), r, B(c), DrawTextOptions.Clip | DrawTextOptions.EnableColorFont, MeasuringMode.Natural);
    }

    public void Icon(string glyph, Vector2 center, float size, Color4 c)
    {
        var r = new Rect(center.X - size, center.Y - size, size * 2, size * 2);
        _rt.DrawText(glyph, Format(size, FontWeight.Normal, TextAlignment.Center, ParagraphAlignment.Center, Theme.IconFont), r, B(c),
            DrawTextOptions.None, MeasuringMode.Natural);
    }

    public float Measure(string s, float size, FontWeight w = FontWeight.Normal, string font = Theme.Font)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var key = (font, size, w, s);
        if (_measure.TryGetValue(key, out var v)) return v;
        using var layout = DW.CreateTextLayout(s, Format(size, w, TextAlignment.Leading, ParagraphAlignment.Near, font), 4000, 200);
        v = layout.Metrics.WidthIncludingTrailingWhitespace;
        if (_measure.Count > 512) _measure.Clear();
        _measure[key] = v;
        return v;
    }

    /// <summary>Son çizilen kareyi PNG olarak kaydeder (--snap test modu).</summary>
    public void SavePng(string path, int w, int h) => Png.Write(path, w, h, ReadRgba(w, h));

    public unsafe byte[] ReadRgba(int w, int h)
    {
        var rgba = new byte[w * h * 4];
        byte* src = (byte*)_bits;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            byte* p = src + (y * _dibW + x) * 4;
            int a = p[3], o = (y * w + x) * 4;
            if (a == 0) continue;
            rgba[o] = (byte)Math.Min(255, p[2] * 255 / a);
            rgba[o + 1] = (byte)Math.Min(255, p[1] * 255 / a);
            rgba[o + 2] = (byte)Math.Min(255, p[0] * 255 / a);
            rgba[o + 3] = (byte)a;
        }
        return rgba;
    }

    public void Dispose()
    {
        foreach (var f in _formats.Values) f.Dispose();
        foreach (var v in _linBrushes.Values) v.Dispose();
        foreach (var v in _radBrushes.Values) v.Dispose();
        _brush.Dispose(); _roundCap.Dispose(); _rt.Dispose(); Wic.Dispose(); DW.Dispose(); D2.Dispose();
        FreeSurface();
    }
}
