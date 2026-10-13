using System.Numerics;
using Ada.Core;
using Vortice.DirectWrite;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

public abstract class Panel
{
    protected readonly Island I;
    protected Gfx G => I.G;
    protected Ui U => I.U;

    protected Panel(Island island) => I = island;

    public abstract string Name { get; }
    public abstract string Glyph { get; }
    public abstract void Draw(Rect area);
    public virtual void OnShow() { }
    public virtual void OnHide() { if (_focused != null) { _focused = null; U.FocusId = 0; } }
    /// <summary>Panel açıkken saniyede bir.</summary>
    public virtual void Tick() { }

    // ---------- metin kutusu odağı ----------
    TextBox? _focused;
    int _focusedId;

    /// <summary>Kutuyu çizer; tıklanınca klavye odağı alır, dışarı tıklanınca bırakır. Enter → true.</summary>
    protected void Box(TextBox box, int id, Rect r)
    {
        if (U.Click(id, r))
        {
            _focused = box; _focusedId = id; U.FocusId = id;
            box.Caret = box.Text.Length;
            I.BeginKeyboard();
        }
        else if (U.Pressed && _focused == box && !U.Hit(r)) { Unfocus(); }
        box.Draw(U, id, r, U.FocusId == id && _focused == box);
    }

    protected void Focus(TextBox box, int id)
    {
        _focused = box; _focusedId = id; U.FocusId = id;
        box.Caret = box.Text.Length;
        I.BeginKeyboard();
    }

    protected void Unfocus()
    {
        _focused = null; U.FocusId = 0;
        I.EndKeyboard(true);
    }

    /// <summary>Odaklı kutuda Enter'a basıldı.</summary>
    protected virtual void OnSubmit(TextBox box) { }
    /// <summary>Odaklı kutunun metni değişti.</summary>
    protected virtual void OnTextChanged(TextBox box) { }

    public virtual bool OnChar(char c)
    {
        if (_focused == null || U.FocusId != _focusedId) return false;
        var before = _focused.Text;
        bool r = _focused.OnChar(c);
        if (before != _focused.Text) OnTextChanged(_focused);
        return r;
    }

    public virtual bool OnKey(int vk, bool ctrl)
    {
        if (_focused == null || U.FocusId != _focusedId) return false;
        if (vk == 0x1B) { Unfocus(); return true; }
        var before = _focused.Text;
        if (_focused.OnKey(vk, ctrl, () => Services.ClipboardService.ReadText(I.Hwnd))) OnSubmit(_focused);
        if (before != _focused.Text) OnTextChanged(_focused);
        return vk is not (0x26 or 0x28); // yukarı/aşağı panel listesine kalsın
    }

    // ---------- ortak çizimler ----------
    protected void Card(Rect r, float radius = 18)
    {
        G.FillRound(r, radius, Theme.Surface);
        G.StrokeRound(r, radius, Theme.Rgb(0xFFFFFF, 0.045f), 1);
    }

    protected void Label(string text, float x, float y, float w)
        => G.Text(text, new Rect(x, y, w, 18), 11.5f, Theme.Text2, FontWeight.SemiBold);

    protected void Empty(Rect a, string glyph, string title, string sub)
    {
        var c = new Vector2(a.CenterX, a.CenterY - 26);
        G.FillCircle(c, 28, Theme.Surface);
        G.Icon(glyph, c, 20, Theme.Text2);
        G.Text(title, new Rect(a.X, c.Y + 36, a.Width, 22), 15, Theme.Text, FontWeight.SemiBold, TextAlignment.Center);
        G.Text(sub, new Rect(a.X, c.Y + 58, a.Width, 20), 12.5f, Theme.Text2, FontWeight.Normal, TextAlignment.Center, font: Theme.TextFont);
    }
}
