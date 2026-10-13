using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ada.Core;
using Ada.Data;

namespace Ada.Services.Ai;

public enum ChatRole { User, Assistant, Tool, Info, Error }

/// <summary>Ekranda gösterilen bir baloncuk.</summary>
public sealed class Bubble
{
    public ChatRole Role;
    public string Text = "";
    public string? ToolName;     // araç baloncuğu
    public bool Running;         // araç çalışıyor
    public float Anim;           // beliriş animasyonu
}

/// <summary>Onay bekleyen yıkıcı işlem.</summary>
public sealed class PendingApproval
{
    public required string Tool;
    public required string Summary;
    public required TaskCompletionSource<bool> Result;
}

/// <summary>
/// Çok sağlayıcılı yapay zeka asistanı: konuşmayı yürütür, araçları çağırır, yıkıcı işlemler için UI'dan onay ister.
/// Tüm ağ ve dosya işi arka planda; UI güncellemeleri Dispatcher ile.
/// </summary>
public sealed class AiService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public readonly List<Bubble> Bubbles = new();
    public PendingApproval? Pending { get; private set; }
    public bool Busy { get; private set; }
    public event Action? Changed;

    readonly AiTools _tools;
    readonly List<ToolDef> _toolDefs;
    readonly List<ChatMsg> _history = new();
    CancellationTokenSource? _cts;

    public AiService()
    {
        _tools = new AiTools(WorkDir);
        _toolDefs = _tools.Build();
    }

    public static string WorkDir()
    {
        var d = Store.Current.AiWorkDir;
        if (string.IsNullOrWhiteSpace(d) || !Directory.Exists(d)) d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.GetFullPath(d);
    }

    public bool Configured
    {
        get { var p = Store.Current.Ai(); return Store.Current.AiProvider == "ollama" || p.ApiKey.Trim().Length > 0; }
    }

    void Notify() => Changed?.Invoke();

    public void Reset()
    {
        _history.Clear(); Bubbles.Clear();
        _cts?.Cancel(); Busy = false; Pending = null;
        Notify();
    }

    public void Cancel()
    {
        _cts?.Cancel();
        ResolvePending(false);
    }

    public void Send(string text)
    {
        if (Busy || string.IsNullOrWhiteSpace(text)) return;
        var prov = Providers.Get(Store.Current.AiProvider);
        var prof = Store.Current.Ai();
        Bubbles.Add(new Bubble { Role = ChatRole.User, Text = text.Trim() });
        _history.Add(Protocol.UserMessage(prov.Kind, text.Trim()));
        Busy = true;
        _cts = new CancellationTokenSource();
        Notify();
        _ = RunLoop(prov, prof, _cts.Token);
    }

    // ---------------- onay ----------------
    public void Approve(bool ok) => ResolvePending(ok);

    void ResolvePending(bool ok)
    {
        var p = Pending; Pending = null;
        p?.Result.TrySetResult(ok);
        Notify();
    }

    async Task<bool> AskApproval(string tool, string summary, CancellationToken ct)
    {
        if (!Store.Current.AiConfirm) return true;
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.Post(() => { Pending = new PendingApproval { Tool = tool, Summary = summary, Result = tcs }; Notify(); });
        using (ct.Register(() => tcs.TrySetResult(false)))
            return await tcs.Task;
    }

    // ---------------- agent döngüsü ----------------
    async Task RunLoop(ProviderInfo prov, AiProfile prof, CancellationToken ct)
    {
        var proto = Protocol.For(prov.Kind);
        bool comp = Store.Current.AiComputerUse;
        var tools = _toolDefs.Where(t =>
            (Store.Current.AiWebSearch || t.Name is not ("web_search" or "fetch_url")) &&
            (comp || !AiTools.ComputerTools.Contains(t.Name))).ToList();
        var sys = SystemPrompt();
        try
        {
            for (int step = 0; step < 16 && !ct.IsCancellationRequested; step++)
            {
                var (url, headers, body) = proto.Build(prov, prof, _history, tools, sys);
                var node = await Post(url, headers, body, ct);
                var turn = proto.Parse(node, _history);

                if (!string.IsNullOrWhiteSpace(turn.Text))
                    Dispatcher.Post(() => { Bubbles.Add(new Bubble { Role = ChatRole.Assistant, Text = turn.Text.Trim() }); Notify(); });

                if (turn.Calls.Count == 0) break;

                var results = new List<ToolResult>();
                bool usedComputer = false;
                foreach (var call in turn.Calls)
                {
                    ct.ThrowIfCancellationRequested();
                    results.Add(await RunTool(call, ct));
                    if (AiTools.ComputerTools.Contains(call.Name)) usedComputer = true;
                }
                proto.AddToolResults(_history, results);

                // bilgisayar eylemlerinden sonra güncel ekranı asistana göster
                if (comp && usedComputer)
                {
                    await Task.Delay(350, ct); // ekranın güncellenmesini bekle
                    var shot = ComputerControl.Capture();
                    if (shot is { } s)
                    {
                        ComputerControl.LastImgW = s.outW; ComputerControl.LastImgH = s.outH;
                        proto.AttachImage(_history, $"Güncel ekran ({s.outW}x{s.outH} piksel; tüm koordinatlar bu boyuta göre).", s.png);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Error("ai", e);
            Dispatcher.Post(() => { Bubbles.Add(new Bubble { Role = ChatRole.Error, Text = "Hata: " + e.Message }); Notify(); });
        }
        finally
        {
            Dispatcher.Post(() => { Busy = false; Notify(); });
        }
    }

    async Task<ToolResult> RunTool(ToolCall call, CancellationToken ct)
    {
        var def = _toolDefs.FirstOrDefault(t => t.Name == call.Name);
        if (def == null) return new ToolResult(call.Id, call.Name, "Bilinmeyen araç: " + call.Name);

        var bubble = new Bubble { Role = ChatRole.Tool, ToolName = call.Name, Running = true, Text = Summarize(call) };
        Dispatcher.Post(() => { Bubbles.Add(bubble); Notify(); });

        bool needApproval = def.Destructive || def.Gate?.Invoke(call.Args) == true;
        if (needApproval)
        {
            var summary = def.Preview?.Invoke(call.Args) ?? Summarize(call);
            bool ok = await AskApproval(call.Name, summary, ct);
            if (!ok)
            {
                Dispatcher.Post(() => { bubble.Running = false; bubble.Text += " — reddedildi"; Notify(); });
                return new ToolResult(call.Id, call.Name, "Kullanıcı bu işlemi reddetti.");
            }
        }

        string result;
        try { result = await def.Run(call.Args, ct); }
        catch (OperationCanceledException) { result = "İptal edildi."; }
        catch (Exception e) { result = "Araç hatası: " + e.Message; Log.Error("tool " + call.Name, e); }

        Dispatcher.Post(() => { bubble.Running = false; Notify(); });
        return new ToolResult(call.Id, call.Name, result.Length > 12000 ? result[..12000] + "\n…(kısaltıldı)" : result);
    }

    static string Summarize(ToolCall c)
    {
        var first = c.Args.FirstOrDefault();
        var val = first.Value?.ToString() ?? "";
        if (val.Length > 60) val = val[..60] + "…";
        return c.Name + (val.Length > 0 ? ": " + val : "");
    }

    static string SystemPrompt()
    {
        var wd = WorkDir();
        var sb = new StringBuilder();
        sb.Append("Sen Ada'sın, Windows bilgisayarında kullanıcıya yardım eden bir asistansın. Türkçe, kısa ve net konuş.\n");
        sb.Append("Araçlarla gerçek işler yapabilirsin: dosya okuma/yazma, geri dönüşüm kutusuna taşıma, geçici dosya temizliği, ");
        sb.Append("güvenlik taraması, PowerShell komutu, uygulama/dosya açma, internet araması.\n");
        sb.Append($"Varsayılan çalışma klasörü: {wd}. Dosya işlemlerinde göreli yolları buna göre çöz.\n");
        if (Store.Current.AiComputerUse)
        {
            sb.Append("Ayrıca bilgisayarı doğrudan kullanabilirsin: screenshot ile ekrana bak, click/type_text/press_keys/scroll ile fare ve klavyeyi kullan. ");
            sb.Append("Böylece WhatsApp'tan mesaj gönderme, bir programı kullanma gibi işleri yapabilirsin. ");
            sb.Append("Her eylemden sonra sana güncel ekran görüntüsü gönderilir; koordinatlar o görüntünün pikselleridir. ");
            sb.Append("Bir uygulama gerekiyorsa önce open_path ya da ara-aç ile başlat, yüklenmesini wait ile bekle, sonra ekrana bakıp devam et. ");
            sb.Append("Adım adım ilerle: her adımda ekranı kontrol et, acele etme. Bir mesajı GÖNDERMEDEN önce ne göndereceğini kullanıcıya bir cümleyle söyle.\n");
        }
        sb.Append("Yıkıcı işlemler (silme, yazma, komut) kullanıcı onayından geçer; reddedilirse başka yol öner.\n");
        sb.Append("Bir şeyi yapmadan önce kısaca ne yapacağını söyle. İşin bitince sonucu bir cümleyle özetle. ");
        sb.Append("Emin olmadığında kullanıcıya sor, uydurma.");
        return sb.ToString();
    }

    async Task<JsonNode> Post(string url, (string, string)[] headers, JsonObject body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        JsonNode? node;
        try { node = JsonNode.Parse(text); } catch { node = null; }
        if (!resp.IsSuccessStatusCode)
        {
            var msg = node?["error"]?["message"]?.GetValue<string>() ?? node?["error"]?.ToString() ?? text;
            if (msg.Length > 300) msg = msg[..300];
            throw new Exception($"{(int)resp.StatusCode}: {msg}");
        }
        return node ?? throw new Exception("Yanıt çözülemedi");
    }

    /// <summary>Ayarlar penceresi için (unsafe bağlamdan await edemediği için) sonucu geri çağırır.</summary>
    public void RunTest(Action<string> done)
        => _ = Task.Run(async () => { var r = await TestConnection(); Dispatcher.Post(() => done(r)); });

    /// <summary>Ayarlarda "bağlantıyı test et": tek mesaj gönderir.</summary>
    public async Task<string> TestConnection()
    {
        try
        {
            var prov = Providers.Get(Store.Current.AiProvider);
            var prof = Store.Current.Ai();
            var proto = Protocol.For(prov.Kind);
            var msgs = new List<ChatMsg> { Protocol.UserMessage(prov.Kind, "Merhaba, tek kelimeyle yanıtla: çalışıyor musun?") };
            var (url, headers, body) = proto.Build(prov, prof, msgs, Array.Empty<ToolDef>(), "Kısa yanıt ver.");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var node = proto.Parse(await Post(url, headers, body, cts.Token), msgs);
            return "✓ Bağlantı başarılı: " + (node.Text.Trim().Length > 0 ? node.Text.Trim() : "yanıt alındı");
        }
        catch (Exception e) { return "✗ " + e.Message; }
    }
}
