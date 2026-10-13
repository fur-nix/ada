using Ada.Core;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFi;
using Windows.Security.Credentials;

namespace Ada.Services;

/// <summary>Wi-Fi ağ listesi: tara, bağlan (kayıtlı ağ ya da şifreyle), bağlantıyı kes.</summary>
public sealed class WifiService
{
    public sealed record Net(string Ssid, int Bars, bool Secure, bool Connected, WiFiAvailableNetwork Raw);

    WiFiAdapter? _adapter;
    public List<Net> Networks { get; private set; } = new();
    public bool Scanning { get; private set; }
    public string Status { get; private set; } = "";
    public string? Connecting { get; private set; }

    public async void Scan()
    {
        if (Scanning) return;
        Scanning = true; Status = "Ağlar aranıyor…";
        try
        {
            if (await WiFiAdapter.RequestAccessAsync() != WiFiAccessStatus.Allowed)
            {
                Dispatcher.Post(() => { Status = "Erişim izni yok — Ayarlar › Gizlilik › Konum › Masaüstü uygulamaları"; Scanning = false; });
                return;
            }
            var adapters = await WiFiAdapter.FindAllAdaptersAsync();
            _adapter = adapters.FirstOrDefault();
            if (_adapter == null) { Dispatcher.Post(() => { Status = "Wi-Fi bağdaştırıcısı bulunamadı"; Scanning = false; }); return; }
            await _adapter.ScanAsync();
            string? current = null;
            try
            {
                var prof = await _adapter.NetworkAdapter.GetConnectedProfileAsync();
                current = prof?.WlanConnectionProfileDetails?.GetConnectedSsid() ?? prof?.ProfileName;
            }
            catch { }
            var list = _adapter.NetworkReport.AvailableNetworks
                .Where(n => !string.IsNullOrWhiteSpace(n.Ssid))
                .GroupBy(n => n.Ssid)
                .Select(g => g.OrderByDescending(n => n.SignalBars).First())
                .Select(n => new Net(n.Ssid, n.SignalBars,
                    n.SecuritySettings.NetworkAuthenticationType is not (Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211 or Windows.Networking.Connectivity.NetworkAuthenticationType.None),
                    n.Ssid == current, n))
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Bars).ThenBy(n => n.Ssid)
                .ToList();
            Dispatcher.Post(() => { Networks = list; Status = list.Count == 0 ? "Ağ bulunamadı" : ""; Scanning = false; });
        }
        catch (Exception e)
        {
            Log.Error("Wifi.Scan", e);
            Dispatcher.Post(() => { Status = "Tarama başarısız"; Scanning = false; });
        }
    }

    /// <summary>Kayıtlı/açık ağa ya da şifreyle bağlan.</summary>
    public async void Connect(Net n, string? password)
    {
        if (_adapter == null) return;
        Connecting = n.Ssid; Status = $"{n.Ssid} ağına bağlanılıyor…";
        try
        {
            var r = password == null
                ? await _adapter.ConnectAsync(n.Raw, WiFiReconnectionKind.Automatic)
                : await _adapter.ConnectAsync(n.Raw, WiFiReconnectionKind.Automatic, new PasswordCredential { Password = password });
            Dispatcher.Post(() =>
            {
                Connecting = null;
                Status = r.ConnectionStatus switch
                {
                    WiFiConnectionStatus.Success => $"{n.Ssid} ağına bağlandı",
                    WiFiConnectionStatus.InvalidCredential => "Şifre yanlış",
                    WiFiConnectionStatus.NetworkNotAvailable => "Ağ artık erişilebilir değil",
                    WiFiConnectionStatus.Timeout => "Zaman aşımı",
                    _ => "Bağlanılamadı"
                };
                Scan();
            });
        }
        catch (Exception e)
        {
            Log.Error("Wifi.Connect", e);
            Dispatcher.Post(() => { Connecting = null; Status = "Bağlanılamadı"; });
        }
    }

    public void Disconnect()
    {
        try { _adapter?.Disconnect(); Status = "Bağlantı kesildi"; Scan(); }
        catch (Exception e) { Log.Error("Wifi.Disconnect", e); }
    }
}

/// <summary>Eşleşmiş Bluetooth cihazları ve bağlantı durumları.</summary>
public sealed class BluetoothService
{
    public sealed record Dev(string Name, bool Connected, string Kind);
    public List<Dev> Devices { get; private set; } = new();
    public bool Loading { get; private set; }

    const string Classic = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}", Le = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";

    public async void Refresh()
    {
        if (Loading) return;
        Loading = true;
        try
        {
            var aqs = $"(System.Devices.Aep.ProtocolId:=\"{Classic}\" OR System.Devices.Aep.ProtocolId:=\"{Le}\") AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
            var infos = await DeviceInformation.FindAllAsync(aqs, new[] { "System.Devices.Aep.IsConnected" }, DeviceInformationKind.AssociationEndpoint);
            var list = infos
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .GroupBy(i => i.Name)
                .Select(g =>
                {
                    bool conn = g.Any(i => i.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var v) && v is bool b && b);
                    return new Dev(g.Key, conn, KindOf(g.Key));
                })
                .OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
            Dispatcher.Post(() => { Devices = list; Loading = false; });
        }
        catch (Exception e)
        {
            Log.Error("Bluetooth", e);
            Dispatcher.Post(() => Loading = false);
        }
    }

    static string KindOf(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("bud") || n.Contains("head") || n.Contains("kulak") || n.Contains("airpods") || n.Contains("wh-") || n.Contains("jbl") || n.Contains("sound")) return "Kulaklık";
        if (n.Contains("mouse") || n.Contains("fare") || n.Contains("mx ")) return "Fare";
        if (n.Contains("keyboard") || n.Contains("klavye") || n.Contains("keys")) return "Klavye";
        if (n.Contains("controller") || n.Contains("xbox") || n.Contains("dualsense") || n.Contains("gamepad")) return "Oyun kumandası";
        if (n.Contains("phone") || n.Contains("iphone") || n.Contains("galaxy") || n.Contains("redmi")) return "Telefon";
        if (n.Contains("speaker") || n.Contains("hoparlör") || n.Contains("boombox") || n.Contains("partybox")) return "Hoparlör";
        return "Cihaz";
    }
}
