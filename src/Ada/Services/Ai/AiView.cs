using System.Numerics;
using Ada.Core;
using Ada.Data;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Services.Ai;

/// <summary>Sohbet arayüzünü çizer; hem island sekmesi hem bağımsız pencere kullanır. Durumu çağıran tutar.</summary>
public sealed class AiView
{
    public readonly TextBox Box = new();
    public Spring Scroll = new(0, 220, 30);
    public bool Stick = true;

    /// <returns>true → giriş kutusuna odak istendi (çağıran BeginKeyboard yapar).</returns>
    public bool Draw(Ui U, Gfx G, AiService ai, Rect a, bool focused, Action submit, Action openSettings)
    {
        bool focusRequested = false;
        if (!ai.Configured) { DrawSetup(U, G, a, openSettings); return false; }

        float inputH = 42, barH = ai.Pending != null ? 56 : 0;
        var list = new Rect(a.X, a.Y, a.Width, a.Height - inputH - 10 - barH - (barH > 0 ? 8 : 0));

        if (ai.Bubbles.Count == 0)
            Empty(G, list, "", "Merhaba, ben Ada", "Bilgisayarında iş yapabilirim: dosya düzenle, çöpleri temizle, kod yaz, internette ara, uygulamaları kullan.");

        float contentH = 0;
        foreach (var b in ai.Bubbles) contentH += BubbleHeight(G, b, list.Width) + 8;
        float maxScroll = MathF.Max(0, contentH - list.Height);
        if (U.Hit(list) && U.Wheel != 0) { Scroll.Target = Math.Clamp(Scroll.Target - U.Wheel * 60, 0, maxScroll); Stick = Scroll.Target >= maxScroll - 4; }
        if (Stick) Scroll.Target = maxScroll;
        Scroll.Target = Math.Clamp(Scroll.Target, 0, maxScroll);
        Scroll.Step(U.Dt);
        if (!Scroll.Settled) U.NeedsFrame = true;

        G.PushClip(list);
        float y = list.Y - Scroll.Value;
        foreach (var b in ai.Bubbles)
        {
            float h = BubbleHeight(G, b, list.Width);
            if (y + h >= list.Y - 4 && y <= list.Bottom + 4) DrawBubble(U, G, b, list, y, h);
            y += h + 8;
        }
        G.PopClip();

        float cy = list.Bottom + 8;
        if (ai.Pending is { } pend)
        {
            var bar = new Rect(a.X, cy, a.Width, barH);
            G.FillRound(bar, 14, Theme.Orange.WithA(0.14f));
            G.StrokeRound(bar, 14, Theme.Orange.WithA(0.5f), 1);
            G.Icon("", new Vector2(bar.X + 22, bar.CenterY), 14, Theme.Orange);
            G.Text("Onay: " + pend.Summary, new Rect(bar.X + 42, bar.Y + 8, bar.Width - 230, barH - 16), 12, Theme.Text, FontWeight.SemiBold, wrap: true);
            if (U.Pill(Ui.Id("aiv.deny"), new Rect(bar.Right - 200, bar.CenterY - 15, 86, 30), "Reddet", Theme.Surface, Theme.Text)) ai.Approve(false);
            if (U.Pill(Ui.Id("aiv.allow"), new Rect(bar.Right - 104, bar.CenterY - 15, 92, 30), "İzin ver", Theme.Orange, Theme.Rgb(0x2A1608))) ai.Approve(true);
            cy = bar.Bottom + 8;
        }

        int boxId = Ui.Id("aiv.box");
        var box = new Rect(a.X, cy, a.Width - (ai.Busy ? 92 : 46), inputH);
        if (U.Click(boxId, box)) focusRequested = true;
        Box.Draw(U, boxId, box, focused);
        if (ai.Busy)
        {
            if (U.Pill(Ui.Id("aiv.stop"), new Rect(a.Right - 86, cy + 5, 86, 32), "Durdur", Theme.Surface, Theme.Red)) ai.Cancel();
            float t = U.Time * 3;
            for (int i = 0; i < 3; i++)
                G.FillCircle(new Vector2(box.Right - 60 + i * 12, box.CenterY), 2.5f + 1.5f * MathF.Max(0, MathF.Sin(t - i * 0.6f)), Theme.Accent);
            U.NeedsFrame = true;
        }
        else if (U.IconButton(Ui.Id("aiv.send"), new Vector2(a.Right - 22, box.CenterY), 16, "", 13, Theme.Rgb(0x0B0D12), true, Theme.Accent))
        {
            if (Box.Text.Length > 0) submit(); else focusRequested = true;
        }
        return focusRequested;
    }

    float BubbleHeight(Gfx g, Bubble b, float w)
    {
        if (b.Role == ChatRole.Tool) return 30;
        float tw = w * 0.82f - 28;
        return 16 + Math.Max(1, Lines(b.Text, tw)) * 18;
    }

    static int Lines(string text, float maxW)
    {
        int perLine = Math.Max(8, (int)(maxW / 7.2f));
        int lines = 0;
        foreach (var para in text.Split('\n')) lines += Math.Max(1, (int)MathF.Ceiling(para.Length / (float)perLine));
        return Math.Min(lines, 80);
    }

    void DrawBubble(Ui U, Gfx G, Bubble b, Rect list, float y, float h)
    {
        if (b.Role == ChatRole.Tool)
        {
            var r = new Rect(list.X + 8, y + 4, list.Width - 16, 24);
            G.FillRound(r, 8, Theme.Rgb(0xFFFFFF, 0.04f));
            if (b.Running) { float t = U.Time * 4; G.Arc(new Vector2(r.X + 16, r.CenterY), 6, t * 57 % 360, 270, Theme.Accent, 2); U.NeedsFrame = true; }
            else G.Icon("", new Vector2(r.X + 16, r.CenterY), 10, Theme.Green);
            G.Text(b.Text, new Rect(r.X + 32, r.Y, r.Width - 40, 24), 11.5f, Theme.Text2, FontWeight.SemiBold);
            return;
        }
        bool user = b.Role == ChatRole.User;
        var color = b.Role switch { ChatRole.User => Theme.Accent, ChatRole.Error => Theme.Red.WithA(0.2f), _ => Theme.Surface };
        float bw = list.Width * 0.82f;
        var rect = user ? new Rect(list.Right - bw, y, bw, h - 4) : new Rect(list.X, y, bw, h - 4);
        G.FillRound(rect, 14, color);
        var fg = user ? Theme.Rgb(0x0B140D) : b.Role == ChatRole.Error ? Theme.Red : Theme.Text;
        G.Text(b.Text, new Rect(rect.X + 14, rect.Y + 8, rect.Width - 28, rect.Height - 12), 13, fg,
            user ? FontWeight.SemiBold : FontWeight.Normal, font: user ? Theme.Font : Theme.TextFont, wrap: true);
    }

    static void Empty(Gfx G, Rect a, string glyph, string title, string sub)
    {
        var c = new Vector2(a.CenterX, a.CenterY - 26);
        G.FillCircle(c, 28, Theme.Surface);
        G.Icon(glyph, c, 20, Theme.Accent);
        G.Text(title, new Rect(a.X, c.Y + 36, a.Width, 22), 15, Theme.Text, FontWeight.SemiBold, TextAlignment.Center);
        G.Text(sub, new Rect(a.CenterX - 220, c.Y + 58, 440, 40), 12.5f, Theme.Text2, FontWeight.Normal, TextAlignment.Center, font: Theme.TextFont, wrap: true);
    }

    void DrawSetup(Ui U, Gfx G, Rect a, Action openSettings)
    {
        var c = new Vector2(a.CenterX, a.Y + 44);
        G.FillCircle(c, 30, Theme.Accent.WithA(0.18f));
        G.Icon("", c, 22, Theme.Accent);
        G.Text("Yapay zeka asistanını bağla", new Rect(a.X, c.Y + 42, a.Width, 24), 16, Theme.Text, FontWeight.Bold, TextAlignment.Center);
        G.Text("Ücretsiz seçenekler var: Google Gemini (kart gerekmez), Groq, ya da bilgisayarında Ollama.",
            new Rect(a.CenterX - 220, c.Y + 68, 440, 36), 12, Theme.Text2, FontWeight.Normal, TextAlignment.Center, font: Theme.TextFont, wrap: true);
        if (U.Pill(Ui.Id("aiv.setup"), new Rect(a.CenterX - 90, c.Y + 112, 180, 34), "Ayarları aç", Theme.Accent, Theme.Rgb(0x0B140D), ""))
            openSettings();
    }
}
