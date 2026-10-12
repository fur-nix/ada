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
    IntPtr _memDc, _dib, _oldBmp;
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
            new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96, RenderTargetUsage.None, FeatureLevel.Default);
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
        _dib = Win32.CreateDIBSection(_memDc, ref bmi, 0, out _, IntPtr.Zero, 0);
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

    public void Begin(int pw, int ph)
    {
        EnsureSurface(pw, ph);
        _rt.BindDC(_memDc, new RawRect(0, 0, pw, ph));
        _rt.BeginDraw();
        _rt.Transform = Matrix3x2.Identity;
        _rt.Clear(new Color4(0, 0, 0, 0));
        Alpha = 1f;
    }

    public void End()
    {
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
            using var stops = _rt.CreateGradientStopCollection(new[]
            {
                new GradientStop(0, c.WithA(0.32f * Alpha)), new GradientStop(1, c.WithA(0f))
            });
            using var lg = _rt.CreateLinearGradientBrush(new LinearGradientBrushProperties(new Vector2(0, r.Top), new Vector2(0, r.Bottom)), stops);
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
        using var stops = _rt.CreateGradientStopCollection(new[]
        {
            new GradientStop(0, top.WithA(Alpha)), new GradientStop(1, bottom.WithA(Alpha))
        });
        using var lg = _rt.CreateLinearGradientBrush(new LinearGradientBrushProperties(new Vector2(0, r.Top), new Vector2(0, r.Bottom)), stops);
        rad = MathF.Min(rad, MathF.Min(r.Width, r.Height) / 2);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(r), rad, rad), lg);
    }

    public void HGradientRound(Rect r, float rad, Color4 left, Color4 right)
    {
        using var stops = _rt.CreateGradientStopCollection(new[]
        {
            new GradientStop(0, left.WithA(Alpha)), new GradientStop(1, right.WithA(Alpha))
        });
        using var lg = _rt.CreateLinearGradientBrush(new LinearGradientBrushProperties(new Vector2(r.Left, 0), new Vector2(r.Right, 0)), stops);
        rad = MathF.Min(rad, MathF.Min(r.Width, r.Height) / 2);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(r), rad, rad), lg);
    }

    /// <summary>Yumuşak radyal ışıma (renk → şeffaf).</summary>
    public void Glow(Vector2 center, float rx, float ry, Color4 c, Rect clip, float clipRadius)
    {
        using var stops = _rt.CreateGradientStopCollection(new[]
        {
            new GradientStop(0, c.WithA(Alpha)), new GradientStop(1, c.WithA(0))
        });
        using var rg = _rt.CreateRadialGradientBrush(new RadialGradientBrushProperties
        {
            Center = center, RadiusX = rx, RadiusY = ry, GradientOriginOffset = Vector2.Zero
        }, stops);
        _rt.FillRoundedRectangle(new RoundedRectangle(RF(clip), clipRadius, clipRadius), rg);
    }

    /// <summary>Katmanlı yumuşak gölge.</summary>
    public void Shadow(Rect r, float rad, float spread, float strength, float dy = 4)
    {
        const int N = 10;
        for (int i = N; i >= 1; i--)
        {
            float t = i / (float)N;
            var rr = new Rect(r.X - spread * t, r.Y - spread * t + dy * t, r.Width + 2 * spread * t, r.Height + 2 * spread * t);
            float a = strength * (1 - t) * (1 - t) / N * 3f;
            FillRound(rr, rad + spread * t, new Color4(0, 0, 0, a));
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

    public ID2D1Bitmap? FromHBitmap(IntPtr hbm, int maxPx)
    {
        try
        {
            using var src = Wic.CreateBitmapFromHBITMAP(hbm, IntPtr.Zero, BitmapAlphaChannelOption.UsePremultipliedAlpha);
            return FromSource(src, maxPx, out _);
        }
        catch (Exception e) { Log.Error("FromHBitmap", e); return null; }
    }

    ID2D1Bitmap FromSource(IWICBitmapSource src, int maxPx, out Color4 dominant)
    {
        var size = src.Size;
        float k = MathF.Min(1f, maxPx / (float)Math.Max(size.Width, size.Height));
        uint w = (uint)Math.Max(1, size.Width * k), h = (uint)Math.Max(1, size.Height * k);
        using var scaler = Wic.CreateBitmapScaler();
        scaler.Initialize(src, w, h, Vortice.WIC.BitmapInterpolationMode.Fant);
        using var conv = Wic.CreateFormatConverter();
        conv.Initialize(scaler, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapDitherType.None, null, 0, BitmapPaletteType.Custom);
        dominant = Dominant(conv, (int)w, (int)h);
        return _rt.CreateBitmapFromWicBitmap(conv, null);
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

    public void Dispose()
    {
        foreach (var f in _formats.Values) f.Dispose();
        _brush.Dispose(); _roundCap.Dispose(); _rt.Dispose(); Wic.Dispose(); DW.Dispose(); D2.Dispose();
        FreeSurface();
    }
}
