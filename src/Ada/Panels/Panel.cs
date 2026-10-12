using Ada.Core;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

public abstract class Panel
{
    protected readonly Island I;
    protected Gfx G => I.G;
    protected Ui U => I.U;

    protected Panel(Island island) => I = island;

    public abstract void Draw(Rect area);
    public virtual void OnShow() { }
    public virtual void OnHide() { }
    /// <summary>Panel açıkken saniyede bir.</summary>
    public virtual void Tick() { }
    public virtual bool OnChar(char c) => false;
    public virtual bool OnKey(int vk, bool ctrl) => false;

    /// <summary>Kart arka planı.</summary>
    protected void Card(Rect r, float radius = 18)
    {
        G.FillRound(r, radius, Theme.Surface);
        G.StrokeRound(r, radius, Theme.Rgb(0xFFFFFF, 0.045f), 1);
    }

    /// <summary>Boş durum: ikon + başlık + açıklama.</summary>
    protected void Empty(Rect a, string glyph, string title, string sub)
    {
        var c = new System.Numerics.Vector2(a.CenterX, a.CenterY - 26);
        G.FillCircle(c, 28, Theme.Surface);
        G.Icon(glyph, c, 20, Theme.Text2);
        G.Text(title, new Rect(a.X, c.Y + 36, a.Width, 22), 15, Theme.Text, Vortice.DirectWrite.FontWeight.SemiBold, Vortice.DirectWrite.TextAlignment.Center);
        G.Text(sub, new Rect(a.X, c.Y + 58, a.Width, 20), 12.5f, Theme.Text2, Vortice.DirectWrite.FontWeight.Normal, Vortice.DirectWrite.TextAlignment.Center, font: Theme.TextFont);
    }
}
