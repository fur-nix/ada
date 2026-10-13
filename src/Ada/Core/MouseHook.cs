using System.Runtime.InteropServices;
using Ada.Native;

namespace Ada.Core;

/// <summary>
/// Sağ tuşu basılı tutunca dairesel menü. Sistem geneli fare kancası:
/// sağ basışı bir an tutar; kısa tıklamaysa ya da sürükleme başlarsa olayı aynen geri gönderir (normal sağ tık bozulmaz),
/// ~250 ms basılı kalırsa menüyü açar. Tam ekran uygulamada/oyunda hiç karışmaz.
/// </summary>
public static unsafe class MouseHook
{
    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public uint type, pad; public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; } // x64: 40 bayt (birlik 8'e hizalı)

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookExW(int id, delegate* unmanaged<int, IntPtr, IntPtr, IntPtr> proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, MOUSEINPUT* inputs, int size);

    const uint WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205;
    static readonly IntPtr Marker = new(0x0ADA0ADA); // kendi gönderdiğimiz olaylar
    public const uint WM_HOOK = Win32.WM_APP + 20;   // ada penceresine: 1 bekleyen basışı geri gönder, 2 tıklamayı geri gönder, 3 bırakıldı, 4 dışarı tıklandı
    public const int HOLD_MS = 250;

    static IntPtr _hook, _target;
    static bool _pending, _held;
    static POINT _downPt;

    /// <summary>Menü açılabilir mi (ayar açık, tam ekran değil vb.) — Island doldurur.</summary>
    public static Func<bool> CanOpen = () => false;
    /// <summary>Menü şu an açık mı ve imleç halkanın üstünde mi — dışarı tıklamayı yakalamak için.</summary>
    public static Func<POINT, bool> RadialOpenAndOutside = _ => false;

    public static bool Installed => _hook != IntPtr.Zero;

    public static void Install(IntPtr target)
    {
        _target = target;
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookExW(14, &Proc, Win32.GetModuleHandleW(IntPtr.Zero), 0); // WH_MOUSE_LL
        if (_hook == IntPtr.Zero) Log.Info("Fare kancası kurulamadı");
    }

    public static void Uninstall()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _pending = _held = false;
    }

    public static POINT DownPoint => _downPt;
    public static bool Pending => _pending;

    /// <summary>Zamanlayıcı doldu ve tuş hâlâ basılı → menü açılıyor.</summary>
    public static bool BeginHold()
    {
        if (!_pending) return false;
        _pending = false; _held = true;
        return true;
    }

    [UnmanagedCallersOnly]
    static IntPtr Proc(int code, IntPtr w, IntPtr l)
    {
        try
        {
            if (code >= 0)
            {
                var info = (MSLLHOOKSTRUCT*)l;
                if (info->dwExtraInfo != Marker)
                {
                    uint msg = (uint)w;
                    switch (msg)
                    {
                        case WM_RBUTTONDOWN when !_pending && !_held && CanOpen():
                            _pending = true; _downPt = info->pt;
                            Win32.SetTimer(_target, 8801, HOLD_MS, IntPtr.Zero);
                            return 1; // şimdilik yut
                        case WM_MOUSEMOVE when _pending:
                            int dx = info->pt.X - _downPt.X, dy = info->pt.Y - _downPt.Y;
                            if (dx * dx + dy * dy > 64) // sağ tuşla sürükleme → olayı geri ver
                            {
                                _pending = false;
                                Win32.KillTimer(_target, 8801);
                                Win32.PostMessageW(_target, WM_HOOK, 1, 0);
                            }
                            break;
                        case WM_RBUTTONUP when _pending: // kısa tıklama → aynen geri gönder
                            _pending = false;
                            Win32.KillTimer(_target, 8801);
                            Win32.PostMessageW(_target, WM_HOOK, 2, 0);
                            return 1;
                        case WM_RBUTTONUP when _held: // menü açıkken bırakıldı
                            _held = false;
                            Win32.PostMessageW(_target, WM_HOOK, 3, 0);
                            return 1;
                        case WM_LBUTTONDOWN when RadialOpenAndOutside(info->pt):
                            Win32.PostMessageW(_target, WM_HOOK, 4, 0); // boş ekrana tıklandı → kapan (tık geçer)
                            break;
                    }
                }
            }
        }
        catch { }
        return CallNextHookEx(_hook, code, w, l);
    }

    /// <summary>Yuttuğumuz sağ basışı (ve gerekirse bırakmayı) geri gönder.</summary>
    public static void Replay(bool down, bool up)
    {
        var inp = stackalloc MOUSEINPUT[2];
        uint n = 0;
        if (down) inp[n++] = new MOUSEINPUT { type = 0, dwFlags = 0x0008, dwExtraInfo = Marker };
        if (up) inp[n++] = new MOUSEINPUT { type = 0, dwFlags = 0x0010, dwExtraInfo = Marker };
        if (n > 0) SendInput(n, inp, sizeof(MOUSEINPUT));
    }
}
