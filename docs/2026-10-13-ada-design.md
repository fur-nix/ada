# Ada — Windows için çok işlevli "Dynamic Island"

## Amaç
Ekranın üst ortasında duran, Hitodama/iPhone tarzı bir ada. Anime yok, sesli AI yok.
Premium görünüm + yay (spring) animasyonları, çok düşük RAM.

## Başarı ölçütleri
- Boşta CPU ≈ %0, RAM (private working set) < 25 MB hedef.
- Kapalı → açık geçişi yumuşak (vsync'li, ~60 fps), hafif "overshoot"lu yay.
- v1 sekmeleri: Medya · Sistem · Kontroller · Pano · Görevler · Uygulamalar.

## Teknik
- C# / .NET 9, AOT uyumlu kod (makinede C++ araçları kurulunca `-p:Aot=true` ile NativeAOT).
- Tek layered Win32 penceresi (WS_EX_LAYERED|TOOLWINDOW|TOPMOST|NOACTIVATE),
  Direct2D (yazılım render target, DC'ye bağlı) + UpdateLayeredWindow ile per-pixel alpha.
  Pencere her karede adanın boyutuna göre küçülür/büyür → ada dışındaki tıklamalar alttaki pencereye geçer.
- Immediate-mode mini UI katmanı (Ui), animasyonlar Spring + Tween.
- Render döngüsü: animasyon varken PeekMessage + DwmFlush (vsync), yoksa GetMessage ile tamamen uyur.

## Durumlar
- Collapsed: 190×32 hap; saat + (müzik çalıyorsa) kapak ve ekolayzer çubukları.
- Hover: hap hafifçe büyür. Tık / tekerlek aşağı / Ctrl+Alt+Space → Expanded (680×330).
- Live activity: kısa bildirimler (kopyalandı, yeni şarkı, hatırlatıcı) için genişleyen hap.
- Fare çıkınca 350 ms sonra kapanır (yazı yazılıyorsa kapanmaz). Esc kapatır.
- Tam ekran uygulama ön plandayken gizlenir.

## Modüller
- Medya: GlobalSystemMediaTransportControls (WinRT); kapak (WIC), baskın renk ile arka plan ışıması.
- Sistem: CPU (GetSystemTimes), RAM, GPU (PDH), disk, ağ hızı, pil, uptime; sadece sekme açıkken örneklenir.
- Kontroller: ses (Core Audio COM), parlaklık (DDC/CI → yoksa WMI/powershell), Wi-Fi/Bluetooth (WinRT Radios),
  karanlık mod (registry + WM_SETTINGCHANGE), kilitle/uyku/yeniden başlat/kapat (çift tık onayı).
- Pano: son 25 metin (yalnızca bellekte), tıkla → panoya geri kopyala.
- Görevler: ay takvimi + yapılacaklar; "18:30 toplantı", "30dk çay", "yarın 9:00 x" → hatırlatıcı.
- Uygulamalar: sabitlenmiş uygulama ızgarası, + ile dosya seç, sürükle-bırak ile ekle, sağ tık ile kaldır.
- Kalıcı veri: `%AppData%\Ada\state.json` (System.Text.Json source generator).

## Kapsam dışı (v2)
Harici monitör için WMI'siz parlaklık iyileştirmesi, pano görselleri, takvim senkronu, eklenti sistemi.

## Test
Saf mantık (Spring, ReminderParser, ClipboardHistory, State JSON) xUnit ile;
uygulama çalıştırılıp ekran görüntüsü ve RAM ölçümüyle doğrulanır.
