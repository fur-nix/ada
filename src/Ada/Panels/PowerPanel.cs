using System.Numerics;
using Ada.Core;
using Ada.Data;
using Ada.Services;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>
/// Güç: Performans (ASUS fan profili, güç planı/modu, pil sınırı) · Ekran kartı (NVIDIA, GPU modu, uygulama başına GPU)
/// · Ekran (yenileme hızı, overdrive, parlaklık) · Oturum (kilitle/uyku/yeniden başlat/kapat).
/// </summary>
public sealed class PowerPanel : Panel
{
    int _sub;
    string? _confirm;
    DateTime _confirmUntil;
    readonly NvidiaInfo _nv = new();
    List<PowerPlans.Plan> _plans = new();
    Guid _activePlan;
    int _overlay = -1, _cpuFan = -1, _gpuFan = -1, _cpuTemp = -1, _gpuEco = -1, _gpuMux = -1, _overdrive = -1;
    List<int> _rates = new();
    int _hz = -1;
    int _tick;

    public PowerPanel(Island i) : base(i) { }

    /// <summary>--snap testi için alt sekme seçimi.</summary>
    public void SetSub(int s) => _sub = s;

    public override string Name => "Güç";
    public override string Glyph => Core.Glyph.Power;

    public override void OnShow()
    {
        _plans = PowerPlans.List();
        _activePlan = PowerPlans.Active();
        _overlay = PowerPlans.Overlay();
        _rates = DisplayModes.Rates();
        _hz = DisplayModes.Current();
        ReadAsus(full: true);
        _nv.Refresh();
        I.Stats.RefreshTopApps();
        I.Controls.ReadBrightness(I.Monitor);
    }

    void ReadAsus(bool full)
    {
        if (!AsusAcpi.Available) return;
        _cpuFan = AsusAcpi.FanRpm(AsusAcpi.CpuFan);
        _gpuFan = AsusAcpi.FanRpm(AsusAcpi.GpuFan);
        _cpuTemp = AsusAcpi.CpuTempC();
        if (!full) return;
        _gpuEco = AsusAcpi.Get(AsusAcpi.GpuEco);
        _gpuMux = AsusAcpi.Get(AsusAcpi.GpuMux);
        _overdrive = AsusAcpi.Get(AsusAcpi.Overdrive);
    }

    public override void Tick()
    {
        ReadAsus(full: false);
        if (++_tick % 2 == 0 && _sub == 1) _nv.Refresh();
        if (_confirm != null && DateTime.Now > _confirmUntil) _confirm = null;
        I.Invalidate();
    }

    bool Confirm(string key)
    {
        if (_confirm == key) { _confirm = null; return true; }
        _confirm = key; _confirmUntil = DateTime.Now.AddSeconds(4);
        return false;
    }

    public override void Draw(Rect a)
    {
        _sub = U.Segmented(Ui.Id("pw.sub"), new Rect(a.X, a.Y, 460, 30), new[] { "Performans", "Ekran kartı", "Ekran", "Oturum" }, _sub);
        var body = new Rect(a.X, a.Y + 42, a.Width, a.Height - 42);
        switch (_sub)
        {
            case 0: DrawPerformance(body); break;
            case 1: DrawGpu(body); break;
            case 2: DrawDisplay(body); break;
            default: DrawSession(body); break;
        }
    }

    void Module(Rect r)
    {
        G.VGradientRound(r, 16, Theme.Rgb(0xFFFFFF, 0.08f), Theme.Rgb(0xFFFFFF, 0.04f));
        G.StrokeRound(r, 16, Theme.Rgb(0xFFFFFF, 0.07f), 1);
    }

    // ================= Performans =================
    void DrawPerformance(Rect a)
    {
        var st = Store.Current;
        float lw = a.Width * 0.56f;
        var left = new Rect(a.X, a.Y, lw, a.Height);
        var right = new Rect(a.X + lw + 12, a.Y, a.Width - lw - 12, a.Height);

        Module(left);
        G.Text("Fan ve performans", new Rect(left.X + 14, left.Y + 8, 200, 18), 12, Theme.Text, FontWeight.Bold);
        if (!AsusAcpi.Available)
        {
            G.Text("Bu cihazda ASUS kontrol arayüzü yok", new Rect(left.X, left.Y, left.Width, left.Height), 11.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
        }
        else
        {
            G.Text("ASUS", new Rect(left.Right - 60, left.Y + 8, 46, 18), 10, Theme.Text3, FontWeight.Bold, TextAlignment.Trailing);
            // 0 dengeli, 1 turbo, 2 sessiz (ASUS sırası) → ekranda Sessiz · Dengeli · Turbo
            (int mode, string name, string sub, string glyph, Color4 col)[] modes =
            {
                (2, "Sessiz", "Serin ve sessiz", "", Theme.Sky),
                (0, "Dengeli", "Günlük kullanım", "", Theme.Green),
                (1, "Turbo", "En yüksek güç", "", Theme.Red),
            };
            float cw = (left.Width - 28 - 16) / 3, cy = left.Y + 32, ch = 92;
            for (int i = 0; i < 3; i++)
            {
                var (mode, name, sub, glyph, col) = modes[i];
                var r = new Rect(left.X + 14 + i * (cw + 8), cy, cw, ch);
                int id = Ui.Id("pw.fan", mode);
                bool sel = st.AsusFanMode == mode;
                float hv = U.Anim(id, U.Hit(r) ? 1 : 0), on = U.Anim(id + 3, sel ? 1 : 0, 14);
                G.FillRound(r, 14, Theme.Mix(Theme.Rgb(0xFFFFFF, 0.05f + 0.04f * hv), col.WithA(0.22f), on));
                if (on > 0.01f) G.StrokeRound(r, 14, col.WithA(0.8f * on), 1.5f);
                var ic = new Vector2(r.CenterX, r.Y + 28);
                G.FillCircle(ic, 16, Theme.Mix(Theme.Rgb(0xFFFFFF, 0.1f), col, on));
                G.Icon(glyph, ic, 14, Theme.Mix(Theme.Text, Theme.Rgb(0x0B0D12), on));
                G.Text(name, new Rect(r.X, r.Y + 50, r.Width, 18), 12.5f, Theme.Text, FontWeight.Bold, TextAlignment.Center);
                G.Text(sub, new Rect(r.X, r.Y + 68, r.Width, 16), 10, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
                if (U.Click(id, r))
                {
                    if (AsusAcpi.Set(AsusAcpi.PerfMode, mode) >= 0) { st.AsusFanMode = mode; Store.Save(); }
                    I.Toast($"Fan profili: {name}", sub, glyph, col, 6, 1.8f);
                }
            }
            // canlı değerler
            float gy = cy + ch + 12, gw = (left.Width - 28 - 16) / 3;
            Gauge(new Rect(left.X + 14, gy, gw, left.Bottom - gy - 12), "CPU fanı", _cpuFan >= 0 ? $"{_cpuFan:N0}" : "—", "rpm", _cpuFan / 6500f, Theme.Sky);
            Gauge(new Rect(left.X + 14 + gw + 8, gy, gw, left.Bottom - gy - 12), "GPU fanı", _gpuFan >= 0 ? $"{_gpuFan:N0}" : "—", "rpm", _gpuFan / 6500f, Theme.Pink);
            Gauge(new Rect(left.X + 14 + (gw + 8) * 2, gy, gw, left.Bottom - gy - 12), "CPU", _cpuTemp >= 0 ? $"{_cpuTemp}" : "—", "°C", _cpuTemp / 100f,
                _cpuTemp > 85 ? Theme.Red : _cpuTemp > 70 ? Theme.Orange : Theme.Green);
        }

        // sağ: Windows güç planı + mod + pil sınırı
        Module(right);
        G.Text("Windows güç planı", new Rect(right.X + 14, right.Y + 8, 200, 18), 12, Theme.Text, FontWeight.Bold);
        float py = right.Y + 32;
        foreach (var p in _plans.Take(5))
        {
            var r = new Rect(right.X + 10, py, right.Width - 20, 28);
            int id = Ui.Id("pw.plan", p.Id.GetHashCode());
            bool sel = p.Id == _activePlan;
            float hv = U.Anim(id, U.Hit(r) ? 1 : 0);
            if (sel || hv > 0.01f) G.FillRound(r, 9, sel ? Theme.Accent.WithA(0.22f) : Theme.Rgb(0xFFFFFF, 0.05f * hv));
            G.StrokeCircle(new Vector2(r.X + 14, r.CenterY), 6, sel ? Theme.Accent : Theme.Text3, 1.5f);
            if (sel) G.FillCircle(new Vector2(r.X + 14, r.CenterY), 3.2f, Theme.Accent);
            G.Text(p.Name, new Rect(r.X + 28, r.Y, r.Width - 32, r.Height), 12, Theme.Text, sel ? FontWeight.Bold : FontWeight.SemiBold);
            if (U.Click(id, r)) { PowerPlans.SetActive(p.Id); _activePlan = PowerPlans.Active(); _overlay = PowerPlans.Overlay(); }
            py += 30;
        }
        if (_activePlan == PowerPlans.Balanced)
        {
            G.Text("Güç modu", new Rect(right.X + 14, py + 4, 120, 16), 10.5f, Theme.Text3, FontWeight.Bold);
            int v = U.Segmented(Ui.Id("pw.ov"), new Rect(right.X + 10, py + 22, right.Width - 20, 28), new[] { "Verimli", "Dengeli", "Performans" }, Math.Max(0, _overlay));
            if (v != _overlay) { PowerPlans.SetOverlay(v); _overlay = PowerPlans.Overlay(); }
            py += 56;
        }
        if (AsusAcpi.Available && right.Bottom - py > 50)
        {
            G.Text("Pil şarj sınırı (pil ömrü için 80%)", new Rect(right.X + 14, py + 4, right.Width - 20, 16), 10.5f, Theme.Text3, FontWeight.Bold);
            int[] lims = { 60, 80, 100 };
            int cur = Array.IndexOf(lims, st.ChargeLimit == 0 ? 100 : st.ChargeLimit);
            int v = U.Segmented(Ui.Id("pw.bat"), new Rect(right.X + 10, py + 22, right.Width - 20, 28), new[] { "%60", "%80", "%100" }, cur);
            if (v != cur && AsusAcpi.Set(AsusAcpi.BatteryLimit, lims[v]) >= 0) { st.ChargeLimit = lims[v]; Store.Save(); }
        }
    }

    void Gauge(Rect r, string label, string value, string unit, float frac, Color4 col)
    {
        G.FillRound(r, 12, Theme.Rgb(0x000000, 0.25f));
        G.Text(label, new Rect(r.X + 10, r.Y + 6, r.Width - 16, 14), 10, Theme.Text3, FontWeight.Bold);
        float vw = G.Measure(value, 18, FontWeight.Bold);
        G.Text(value, new Rect(r.X + 10, r.Y + 20, r.Width - 16, 24), 18, Theme.Text, FontWeight.Bold);
        G.Text(unit, new Rect(r.X + 14 + vw, r.Y + 27, 40, 16), 10, Theme.Text2, FontWeight.SemiBold);
        var br = new Rect(r.X + 10, r.Bottom - 10, r.Width - 20, 4);
        G.FillRound(br, 2, Theme.Rgb(0xFFFFFF, 0.08f));
        float shown = U.Anim(Ui.Id("pw.g", label.GetHashCode()), Math.Clamp(frac, 0, 1), 6);
        if (shown > 0.01f) G.FillRound(new Rect(br.X, br.Y, MathF.Max(4, br.Width * shown), 4), 2, col);
    }

    // ================= Ekran kartı =================
    void DrawGpu(Rect a)
    {
        float lw = a.Width * 0.42f;
        var left = new Rect(a.X, a.Y, lw, a.Height);
        var right = new Rect(a.X + lw + 12, a.Y, a.Width - lw - 12, a.Height);

        Module(left);
        if (!_nv.Available)
            G.Text("NVIDIA ekran kartı bulunamadı", left, 11.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
        else
        {
            G.FillRound(new Rect(left.X + 14, left.Y + 12, 46, 20), 6, Theme.Rgb(0x76B900));
            G.Text("NVIDIA", new Rect(left.X + 14, left.Y + 12, 46, 20), 9.5f, Theme.Rgb(0x0B0D12), FontWeight.Black, TextAlignment.Center);
            G.Text(_nv.Name.Length > 0 ? _nv.Name : "Okunuyor…", new Rect(left.X + 68, left.Y + 10, left.Width - 80, 24), 13, Theme.Text, FontWeight.Bold);
            var c = new Vector2(left.X + 60, left.Y + 96);
            float util = Math.Max(0, _nv.Util) / 100f;
            float shown = U.Anim(Ui.Id("pw.nvu"), util, 6);
            G.StrokeCircle(c, 38, Theme.Rgb(0xFFFFFF, 0.08f), 7);
            G.Arc(c, 38, 0, 360 * shown, Theme.Rgb(0x76B900), 7);
            G.Text(_nv.Util >= 0 ? $"{_nv.Util}%" : "—", new Rect(c.X - 34, c.Y - 12, 68, 24), 16, Theme.Text, FontWeight.Bold, TextAlignment.Center);
            G.Text("kullanım", new Rect(c.X - 34, c.Y + 10, 68, 14), 9.5f, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
            float x = left.X + 120, y = left.Y + 50;
            Info(x, ref y, "Sıcaklık", _nv.Temp >= 0 ? $"{_nv.Temp} °C" : "—");
            Info(x, ref y, "Güç", _nv.Power >= 0 ? $"{_nv.Power:0.0} W" : "—");
            Info(x, ref y, "Saat", _nv.Clock >= 0 ? $"{_nv.Clock} MHz" : "—");
            Info(x, ref y, "VRAM", _nv.MemTotal > 0 ? $"{_nv.MemUsed} / {_nv.MemTotal} MB" : "—");
            G.Text(_nv.Status.Length > 0 ? $"Durum: {_nv.Status}" : "", new Rect(left.X + 14, left.Bottom - 24, left.Width - 28, 16), 10, Theme.Text3, FontWeight.SemiBold);
        }

        // sağ üst: GPU modu (ASUS)
        var mode = new Rect(right.X, right.Y, right.Width, 96);
        Module(mode);
        G.Text("GPU modu", new Rect(mode.X + 14, mode.Y + 8, 200, 18), 12, Theme.Text, FontWeight.Bold);
        if (!AsusAcpi.Available || _gpuEco < 0)
            G.Text("Bu cihazda GPU modu değiştirilemiyor", new Rect(mode.X + 14, mode.Y + 34, mode.Width - 28, 40), 11, Theme.Text3, FontWeight.SemiBold);
        else
        {
            int cur = _gpuMux == 0 ? 2 : _gpuEco == 1 ? 0 : 1;
            string[] desc = { "Eco: harici GPU kapalı, en uzun pil", "Standart: Windows gerektiğinde NVIDIA'yı kullanır", "Ultimate: ekran doğrudan NVIDIA'ya bağlı (yeniden başlatma gerekir)" };
            int v = U.Segmented(Ui.Id("pw.gpu"), new Rect(mode.X + 10, mode.Y + 30, mode.Width - 20, 28), new[] { "Eco", "Standart", "Ultimate" }, cur);
            string? warn = _confirm?.StartsWith("gpu") == true ? "Emin misin? Aynı seçeneğe tekrar tıkla (açık oyun/uygulamalar kapanabilir)" : null;
            G.Text(warn ?? desc[cur], new Rect(mode.X + 14, mode.Y + 64, mode.Width - 28, 24), 10.5f, warn != null ? Theme.Red : Theme.Text3, FontWeight.SemiBold);
            if (v != cur && Confirm("gpu" + v)) SetGpuMode(v, cur);
        }

        // sağ alt: uygulama başına GPU (Windows Grafik ayarları)
        var apps = new Rect(right.X, mode.Bottom + 12, right.Width, right.Bottom - mode.Bottom - 12);
        Module(apps);
        G.Text("Uygulama başına GPU", new Rect(apps.X + 14, apps.Y + 8, 200, 18), 12, Theme.Text, FontWeight.Bold);
        float ay = apps.Y + 32;
        foreach (var t in I.Stats.TopApps.Where(t => t.Path != null).Take(2))
        {
            var r = new Rect(apps.X + 10, ay, apps.Width - 20, 30);
            var ic = I.Apps.Icon(t.Path!, 32);
            if (ic != null) G.Bitmap(ic, new Rect(r.X + 4, r.CenterY - 9, 18, 18));
            G.Text(t.Name, new Rect(r.X + 28, r.Y, r.Width - 250, r.Height), 11.5f, Theme.Text, FontWeight.SemiBold);
            int pref = GpuPreference.Get(t.Path!);
            int v = U.Segmented(Ui.Id("pw.gp", t.Path!.GetHashCode()), new Rect(r.Right - 220, r.Y + 1, 220, 28), new[] { "Otomatik", "Tasarruf", "NVIDIA" }, pref);
            if (v != pref) GpuPreference.Set(t.Path!, v);
            ay += 34;
        }
        G.Text("Değişiklik uygulama yeniden açılınca geçerli olur", new Rect(apps.X + 14, apps.Bottom - 22, apps.Width - 28, 14), 9.5f, Theme.Text3, FontWeight.SemiBold);
    }

    void SetGpuMode(int target, int cur)
    {
        if (target == 2) { AsusAcpi.Set(AsusAcpi.GpuMux, 0); I.Toast("Ultimate modu ayarlandı", "Etkinleşmesi için bilgisayarı yeniden başlat", "", Theme.Orange, 6, 6f); }
        else
        {
            if (cur == 2) { AsusAcpi.Set(AsusAcpi.GpuMux, 1); I.Toast("Karma moda dönülüyor", "Yeniden başlatınca geçerli", "", Theme.Orange, 6, 5f); }
            AsusAcpi.Set(AsusAcpi.GpuEco, target == 0 ? 1 : 0);
            I.Toast(target == 0 ? "Eco modu" : "Standart mod", target == 0 ? "Harici GPU kapatıldı" : "Harici GPU açık", "", Theme.Accent, 6, 2.5f);
        }
        ReadAsus(full: true);
    }

    void Info(float x, ref float y, string k, string v)
    {
        G.Text(k, new Rect(x, y, 80, 16), 10.5f, Theme.Text3, FontWeight.SemiBold);
        G.Text(v, new Rect(x + 74, y, 160, 16), 11.5f, Theme.Text, FontWeight.Bold);
        y += 22;
    }

    // ================= Ekran =================
    void DrawDisplay(Rect a)
    {
        var hzCard = new Rect(a.X, a.Y, a.Width, 116);
        Module(hzCard);
        G.Text("Yenileme hızı", new Rect(hzCard.X + 14, hzCard.Y + 8, 200, 18), 12, Theme.Text, FontWeight.Bold);
        G.Text(_hz > 0 ? $"Şu an {_hz} Hz" : "", new Rect(hzCard.Right - 160, hzCard.Y + 8, 146, 18), 11, Theme.Accent, FontWeight.Bold, TextAlignment.Trailing);
        var rates = _rates.TakeLast(6).ToList();
        if (rates.Count == 0) G.Text("Desteklenen hızlar okunamadı", new Rect(hzCard.X + 14, hzCard.Y + 40, 300, 20), 11, Theme.Text3, FontWeight.SemiBold);
        float cw = rates.Count == 0 ? 0 : MathF.Min(140, (hzCard.Width - 28 - (rates.Count - 1) * 10) / rates.Count);
        for (int i = 0; i < rates.Count; i++)
        {
            int hz = rates[i];
            var r = new Rect(hzCard.X + 14 + i * (cw + 10), hzCard.Y + 34, cw, 68);
            int id = Ui.Id("pw.hz", hz);
            bool sel = hz == _hz;
            float hv = U.Anim(id, U.Hit(r) ? 1 : 0), on = U.Anim(id + 3, sel ? 1 : 0, 14);
            G.FillRound(r, 14, Theme.Mix(Theme.Rgb(0xFFFFFF, 0.05f + 0.04f * hv), Theme.Accent.WithA(0.22f), on));
            if (on > 0.01f) G.StrokeRound(r, 14, Theme.Accent.WithA(0.8f * on), 1.5f);
            G.Text(hz.ToString(), new Rect(r.X, r.Y + 10, r.Width, 30), 24, Theme.Text, FontWeight.Black, TextAlignment.Center);
            G.Text(hz >= 120 ? "Hz · akıcı" : "Hz · tasarruf", new Rect(r.X, r.Y + 42, r.Width, 16), 10, Theme.Text3, FontWeight.SemiBold, TextAlignment.Center);
            if (U.Click(id, r) && !sel)
            {
                int res = DisplayModes.Set(hz);
                _hz = DisplayModes.Current();
                I.Toast(res == 0 ? $"Ekran {hz} Hz" : "Yenileme hızı değiştirilemedi", res == 0 ? "Uygulandı" : $"Kod {res}", "", res == 0 ? Theme.Accent : Theme.Red, 6, 2f);
            }
        }

        float y = hzCard.Bottom + 12, w = (a.Width - 12) / 2, h = a.Bottom - y;
        var od = new Rect(a.X, y, w, h);
        Module(od);
        G.Text("Ekran overdrive", new Rect(od.X + 14, od.Y + 10, w - 90, 18), 12, Theme.Text, FontWeight.Bold);
        G.Text(_overdrive < 0 ? "Bu ekranda yok" : "Hızlı hareketlerde bulanıklığı azaltır", new Rect(od.X + 14, od.Y + 30, w - 90, 30), 10.5f, Theme.Text3, FontWeight.SemiBold, wrap: true);
        if (_overdrive >= 0 && U.Toggle(Ui.Id("pw.od"), new Rect(od.Right - 70, od.Y + 14, 56, 28), _overdrive == 1))
        { AsusAcpi.Set(AsusAcpi.Overdrive, _overdrive == 1 ? 0 : 1); _overdrive = AsusAcpi.Get(AsusAcpi.Overdrive); }

        var br = new Rect(a.X + w + 12, y, w, h);
        Module(br);
        G.Text("Parlaklık", new Rect(br.X + 14, br.Y + 10, 120, 18), 12, Theme.Text, FontWeight.Bold);
        var c = I.Controls;
        if (c.Brightness >= 0)
        {
            float b = c.Brightness;
            if (U.Slider(Ui.Id("pw.bri"), new Rect(br.X + 14, br.Y + 38, br.Width - 28, 30), ref b, Theme.Amber, Core.Glyph.Brightness))
                c.SetBrightness(I.Monitor, b);
        }
        if (U.Pill(Ui.Id("pw.disp"), new Rect(br.Right - 150, br.Bottom - 40, 136, 28), "Ekran ayarları", Theme.Surface, Theme.Text))
        { ControlsService.OpenSettings("ms-settings:display"); I.Collapse(); }
    }

    // ================= Oturum =================
    void DrawSession(Rect a)
    {
        string[] names = { "Kilitle", "Uyku", "Yeniden başlat", "Kapat" };
        string[] glyphs = { Core.Glyph.Lock, Core.Glyph.Moon, Core.Glyph.Restart, Core.Glyph.Power };
        Color4[] cols = { Theme.Text, Theme.Sky, Theme.Amber, Theme.Red };
        float gap = 14, w = (a.Width - gap * 3) / 4, h = MathF.Min(150, a.Height - 24);
        float y = a.Y + (a.Height - h) / 2 - 8;
        for (int i = 0; i < 4; i++)
        {
            var r = new Rect(a.X + i * (w + gap), y, w, h);
            int id = Ui.Id("pw", i);
            bool armed = _confirm == "s" + i;
            bool clicked = U.Click(id, r);
            float hv = U.Anim(id, U.Hit(r) ? 1 : 0);
            float k = U.Anim(id + 9, armed ? 1 : 0, 14);
            float p = U.Anim(id + 3, U.ActiveId == id && U.Down && U.Hit(r) ? 1 : 0, 24);
            float s = 1 - 0.04f * p;
            var rr = new Rect(r.CenterX - r.Width * s / 2, r.CenterY - r.Height * s / 2, r.Width * s, r.Height * s);
            G.FillRound(rr, 22, Theme.Mix(Theme.Surface.WithA(1 + 1.2f * hv), Theme.Red.WithA(0.22f), k));
            if (k > 0.01f) G.StrokeRound(rr, 22, Theme.Red.WithA(0.7f * k), 1.5f);
            var c = new Vector2(rr.CenterX, rr.CenterY - 16);
            G.FillCircle(c, 26 + 2 * hv, Theme.Mix(Theme.SurfaceHi, cols[i].WithA(0.25f), hv));
            G.Icon(glyphs[i], c, 20, armed ? Theme.Red : cols[i]);
            G.Text(armed ? "Emin misin? Tekrar tıkla" : names[i], new Rect(rr.X + 6, c.Y + 36, rr.Width - 12, 20), 12.5f,
                armed ? Theme.Red : Theme.Text, FontWeight.Bold, TextAlignment.Center);
            if (clicked)
            {
                if (i == 0) { I.Collapse(); ControlsService.Lock(); }
                else if (i == 1) { I.Collapse(); ControlsService.Sleep(); }
                else if (Confirm("s" + i)) { if (i == 2) ControlsService.Restart(); else ControlsService.Shutdown(); }
            }
        }
    }
}
