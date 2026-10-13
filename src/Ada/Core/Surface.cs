namespace Ada.Core;

/// <summary>
/// Adanın dışındaki ek pencereler (Ayarlar, Spotlight, Dock). Hepsi adanın Gfx'ini ve mesaj döngüsünü paylaşır:
/// ayrı süreç/çizim motoru yok → ek RAM çok az.
/// </summary>
public interface ISurface
{
    IntPtr Hwnd { get; }
    bool Animating { get; }
    void Invalidate();
    void FrameIfNeeded();
    IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    /// <summary>Adanın saniyelik zamanlayıcısı.</summary>
    void Tick() { }
}
