using Ada.Core;
using Ada.Data;
using Ada.Native;
using Ada.Services;

namespace Ada;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Error("unhandled", ex);
            try { Taskbar.SetHidden(false); } catch { } // çökse bile görev çubuğu geri gelsin
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("task", e.Exception); e.SetObserved(); };

        // Ada.exe --make-icon ada.ico : uygulama ikonunu üretir
        if (args.Length >= 2 && args[0] == "--make-icon") { Shell.MakeIcon(args[1]); return 0; }

        // Ada.exe --snap <klasör> : her durumu ekran dışında PNG'ye çizer (gerçek veriye dokunmaz)
        if (args.Length >= 2 && args[0] == "--snap")
        {
            Store.Dir = Path.Combine(Path.GetTempPath(), "ada-snap-" + Environment.ProcessId);
            Directory.CreateDirectory(Store.Dir);
            Store.Load();
            Store.Current.Todos.Add(new TodoItem { Text = "Tasarım incelemesi", Due = DateTime.Today.AddHours(18).AddMinutes(30) });
            Store.Current.Todos.Add(new TodoItem { Text = "Spor salonu", Due = DateTime.Today.AddDays(1).AddHours(9) });
            Store.Current.Todos.Add(new TodoItem { Text = "Faturayı öde" });
            Store.Current.Todos.Add(new TodoItem { Text = "Market alışverişi", Done = true });
            Store.Save();
            var snap = new Island { SnapMode = true };
            snap.Create();
            snap.Snapshot(args[1]);
            Directory.Delete(Store.Dir, true);
            return 0;
        }

        bool openSettings = args.Contains("--settings");
        int pinIdx = Array.IndexOf(args, "--pin");
        string? pinPath = pinIdx >= 0 && pinIdx + 1 < args.Length ? args[pinIdx + 1] : null;
        int radIdx = Array.IndexOf(args, "--radial");
        string? radialPath = radIdx >= 0 && radIdx + 1 < args.Length ? args[radIdx + 1] : null;
        if (radialPath != null) pinPath = null;
        using var mutex = new Mutex(true, @"Local\Ada.Island.SingleInstance", out bool first);
        if (!first)
        {
            // Ada zaten çalışıyor: Başlat menüsündeki "Ada Ayarları" → çalışan kopyaya ayarları açtır
            var h = Win32.FindWindowW("AdaIsland", null);
            string? sendPath = pinPath ?? radialPath;
            if (h != IntPtr.Zero && sendPath != null)
            {
                // Gezgin sağ tık → "Ada Dock'a sabitle": yolu çalışan kopyaya gönder
                unsafe
                {
                    fixed (char* s = sendPath)
                    {
                        var cds = new COPYDATASTRUCT { dwData = radialPath != null ? 2 : 1, cbData = (sendPath.Length + 1) * 2, lpData = (IntPtr)s };
                        Win32.SendMessageW(h, 0x4A, IntPtr.Zero, (IntPtr)(&cds));
                    }
                }
                return 0;
            }
            if (h != IntPtr.Zero)
            {
                Win32.AllowSetForegroundWindow(-1);
                // "Ada Ayarları" → ayarlar penceresi; "Ada" → adayı aç/kapat
                Win32.PostMessageW(h, openSettings ? Island.WM_OPEN_SETTINGS : Win32.WM_HOTKEY, 0, 0);
            }
            return 0;
        }

        var island = new Island();
        island.Create();
        if (Environment.GetEnvironmentVariable("ADA_BENCHLIVE") == "1") { island.BenchLive(); return 0; }
        if (openSettings) island.OpenSettings();
        if (pinPath != null) island.PinToDock(pinPath);
        if (radialPath != null) island.AddToRadial(radialPath);
        try { island.Run(); }
        finally { island.Shutdown(); Store.Save(); }
        return 0;
    }
}
