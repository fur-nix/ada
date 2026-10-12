using Ada.Core;
using Ada.Data;

namespace Ada;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Error("unhandled", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("task", e.Exception); e.SetObserved(); };

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

        using var mutex = new Mutex(true, @"Local\Ada.Island.SingleInstance", out bool first);
        if (!first) return 0; // zaten çalışıyor

        var island = new Island();
        island.Create();
        island.Run();
        Store.Save();
        return 0;
    }
}
