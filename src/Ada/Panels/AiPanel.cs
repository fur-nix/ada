using Ada.Core;
using Ada.Services.Ai;
using Rect = Vortice.Mathematics.Rect;

namespace Ada.Panels;

/// <summary>Island içindeki Yapay Zeka sekmesi — ortak AiView'i kullanır.</summary>
public sealed class AiPanel : Panel
{
    readonly AiView _view = new();
    static readonly int BoxId = Ui.Id("aip.box");

    public AiPanel(Island i) : base(i) => _view.Box.Placeholder = "Ada'ya yaz…";

    public override string Name => "Yapay Zeka";
    public override string Glyph => "";

    public override void Tick() => I.Invalidate();

    protected override void OnSubmit(TextBox box) => Submit();

    void Submit()
    {
        if (string.IsNullOrWhiteSpace(_view.Box.Text)) return;
        if (!I.Ai.Configured) { I.OpenSettings(SettingsWindow.AiPage); return; }
        I.Ai.Send(_view.Box.Text);
        _view.Box.Clear();
        _view.Stick = true;
    }

    public override void Draw(Rect a)
    {
        // AiView kendi kutusunu tutar; panel base'inin TextBox'ı yerine onu kullanıyoruz
        bool focused = U.FocusId == BoxId;
        bool wantFocus = _view.Draw(U, G, I.Ai, a, focused, Submit, () => I.OpenSettings(SettingsWindow.AiPage));
        if (wantFocus) { _focusBox = _view.Box; U.FocusId = BoxId; _view.Box.Caret = _view.Box.Text.Length; I.BeginKeyboard(); }
        else if (U.Pressed && focused && !new Rect(a.X, a.Bottom - 52, a.Width, 52).Contains(U.Mouse)) { U.FocusId = 0; I.EndKeyboard(true); }
    }

    TextBox? _focusBox;

    public override bool OnChar(char c)
    {
        if (U.FocusId != BoxId) return false;
        return _view.Box.OnChar(c);
    }

    public override bool OnKey(int vk, bool ctrl)
    {
        if (U.FocusId != BoxId) return false;
        if (vk == 0x1B) { U.FocusId = 0; I.EndKeyboard(true); return true; }
        if (_view.Box.OnKey(vk, ctrl, () => Services.ClipboardService.ReadText(I.Hwnd))) Submit();
        return vk is not (0x26 or 0x28);
    }
}
