using System.Runtime.InteropServices;
using Ada.Native;

namespace Ada.Core;

/// <summary>
/// Windows tuşuyla Spotlight: tek (yalın) Win basışını yakalar, Başlat menüsünü açmadan Spotlight'ı tetikler.
/// Win+E / Win+D gibi kombinasyonlar bozulmaz. Kancayı asla yutmaz (tuş sıkışması olmaz); Başlat'ı Ctrl
/// dokunuşuyla iptal eder — böylece Win-up yalın kalmaz ve menü açılmaz. Tam ekran oyunda kaldırılır.
/// </summary>
public static unsafe class KeyboardHook
{
    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KINPUT { public uint type; public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; public ulong pad; }

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookExW(int id, delegate* unmanaged<int, IntPtr, IntPtr, IntPtr> proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, KINPUT* inputs, int size);

    const uint WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const uint VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_CONTROL = 0x11;
    static readonly IntPtr Marker = new(0x0ADA0ADB);
    public const uint WM_WINTAP = Win32.WM_APP + 21; // ada penceresine: yalın Win basıldı → Spotlight

    static IntPtr _hook, _target;
    static bool _winDown, _combo;

    public static Func<bool> CanOpen = () => false;
    public static bool Installed => _hook != IntPtr.Zero;

    public static void Install(IntPtr target)
    {
        _target = target;
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookExW(13, &Proc, Win32.GetModuleHandleW(IntPtr.Zero), 0); // WH_KEYBOARD_LL
        if (_hook == IntPtr.Zero) Log.Info("Klavye kancası kurulamadı");
    }

    public static void Uninstall()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _winDown = _combo = false;
    }

    [UnmanagedCallersOnly]
    static IntPtr Proc(int code, IntPtr w, IntPtr l)
    {
        try
        {
            if (code >= 0)
            {
                var info = (KBDLLHOOKSTRUCT*)l;
                if (info->dwExtraInfo != Marker)
                {
                    uint msg = (uint)w, vk = info->vkCode;
                    bool isWin = vk is VK_LWIN or VK_RWIN;
                    if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        if (isWin) { _winDown = true; _combo = false; }
                        else if (_winDown) _combo = true; // Win+başka tuş → kombinasyon, dokunma
                    }
                    else if (msg is WM_KEYUP or WM_SYSKEYUP && isWin && _winDown)
                    {
                        _winDown = false;
                        if (!_combo && CanOpen())
                        {
                            // Başlat'ı iptal et: Ctrl dokunuşu yalın-Win koşulunu bozar → menü açılmaz, Win-up serbest geçer
                            TapCtrl();
                            Win32.PostMessageW(_target, WM_WINTAP, 0, 0);
                        }
                    }
                }
            }
        }
        catch { }
        return CallNextHookEx(_hook, code, w, l);
    }

    static void TapCtrl()
    {
        var arr = stackalloc KINPUT[2];
        arr[0] = new KINPUT { type = 1, wVk = (ushort)VK_CONTROL, dwExtraInfo = Marker };
        arr[1] = new KINPUT { type = 1, wVk = (ushort)VK_CONTROL, dwFlags = 0x0002, dwExtraInfo = Marker };
        SendInput(2, arr, sizeof(KINPUT));
    }
}
