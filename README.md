# Ada — Windows için Dynamic Island

Ekranın üst ortasında duran (MacBook çentiği ya da yüzen ada), çok işlevli, düşük kaynak tüketen bir ada.
Düzen Hitodama'dan esinlenir; görünüm ve davranış **Ada Ayarları** penceresinden özelleştirilir.

**Sekmeler:** Kontroller · Bugün · İstatistik · Pano · Uygulamalar · Duvar Kağıdı · Güç

## Kullanım
- Üzerine gel → büyür. Tıkla / tekerleği aşağı çevir / **Ctrl+Alt+Space** → açılır. **Esc** ya da fareyi çek → kapanır.
- Açıkken **1–7**, **←/→**, **Tab** ile sekme değişir. Sağ üstteki dişli → Ayarlar.
- **Başlat menüsü:** "Ada" (aç/kapat) ve "Ada Ayarları".
- Sağ tık (kapalıyken): Ayarlar, görünüm, Windows ile başlat, çıkış.

## Ayarlar
- **Görünüm:** çentik / yüzen ada, boyut, köşe yuvarlaklığı, vurgu rengi, gölge — canlı önizlemeli.
- **Boşta:** sol / orta / sağ yuva (saat, saniyeli saat, tarih, pil, CPU·RAM, nokta), müzik kapağı + ekolayzer.
- **Davranış:** görünürlük (her zaman / fare üst kenara gelince / sadece kısayol), üzerine gelince aç,
  kapanma gecikmesi, animasyon hızı, tam ekranda gizle, bildirimler (pano, şarkı, hatırlatıcı, ses).
- **Sekmeler:** göster/gizle ve sırala. **Genel:** kısayol, Windows ile başlat, veri klasörü, sıfırla.

## Derleme
```
dotnet publish src/Ada -c Release -r win-x64 --self-contained false -o publish
dotnet publish src/Ada -c Release -r win-x64 -p:Aot=true -o publish-aot   # NativeAOT (MSVC C++ araçları gerekir)
```

## Test
```
dotnet test tests/Ada.Tests
Ada.exe --snap <klasör>      # tüm durumları + ayar sayfalarını ekran dışında PNG'ye çizer (gerçek veriye dokunmaz)
Ada.exe --make-icon ada.ico  # uygulama ikonunu üretir
```
`ADA_DEBUG=1` → saniyelik kare/durum bilgisi `%AppData%\Ada\ada.log`.

## Mimari
Tek layered Win32 penceresi + Direct2D (yazılım) → UpdateLayeredWindow; ayarlar penceresi aynı çizim katmanını paylaşır.
Animasyon varken vsync'li döngü, yokken tamamen uyur. Ayrıntılar: `docs/2026-10-13-ada-design.md`.
