using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Ada.Native;

namespace Ada.Core;

/// <summary>
/// Düşük seviye fare/klavye kancaları için ayrı bir iş parçacığı. Kanca geri çağrıları, kancayı kuran
/// iş parçacığında çalışır; bu yüzden onları render'dan (DwmFlush'ta bloklanan ana iş parçacığı) ayırıyoruz.
/// Bu iş parçacığı yalnızca mesaj pompalar → giriş asla gecikmez (oyunda takılma/fare gecikmesi biter).
/// </summary>
public static class HookThread
{
    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr w, l; public uint time; public int ptx, pty; }
    [DllImport("user32.dll")] static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);

    const uint WM_RUN = Win32.WM_APP + 30;
    static Thread? _t;
    static uint _tid;
    static readonly ConcurrentQueue<Action> Queue = new();
    static readonly ManualResetEventSlim Ready = new(false);

    public static void Ensure()
    {
        if (_t != null) return;
        _t = new Thread(Loop) { IsBackground = true, Name = "AdaHooks", Priority = ThreadPriority.AboveNormal };
        _t.Start();
        Ready.Wait(2000);
    }

    static void Loop()
    {
        _tid = Win32.GetCurrentThreadId();
        // mesaj kuyruğunu oluştur
        Win32.PeekMessageW(out _, IntPtr.Zero, 0, 0, 0);
        Ready.Set();
        while (GetMessageW(out var m, IntPtr.Zero, 0, 0) > 0)
        {
            if (m.message == WM_RUN) while (Queue.TryDequeue(out var a)) { try { a(); } catch (Exception e) { Log.Error("hookthread", e); } }
            TranslateMessage(ref m);
            DispatchMessageW(ref m);
        }
    }

    /// <summary>İşi kanca iş parçacığında çalıştır (SetWindowsHookEx/Unhook burada olmalı).</summary>
    public static void Run(Action a)
    {
        Ensure();
        Queue.Enqueue(a);
        Win32.PostThreadMessageW(_tid, WM_RUN, IntPtr.Zero, IntPtr.Zero);
    }
}
