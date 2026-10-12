# Ada — Windows için Dynamic Island

Ekranın üst ortasında duran, çok işlevli, düşük kaynak tüketen bir ada.

**Sekmeler:** Medya · Sistem · Kontroller · Pano · Görevler · Uygulamalar

## Kullanım
- Üzerine gel → büyür. Tıkla / tekerleği aşağı çevir / **Ctrl+Alt+Space** → açılır.
- Fare çıkınca ya da **Esc** ile kapanır. Açıkken **1–6**, **←/→**, **Tab** ile sekme değişir.
- **Sağ tık** (kapalıyken): Görünüm (Ada / Çentik), Windows ile başlat, veri klasörü, çıkış.
- Kontroller sekmesindeki **Çentik modu** kutucuğu da görünümü değiştirir.
- Görevler: `18:30 toplantı`, `30dk çay`, `yarın 9:00 spor` yazınca hatırlatıcı kurulur.
- Uygulamalar: `+` ile ekle, dosyaları adaya sürükle-bırak, sağ tık ile kaldır.
- Tam ekran oyun/video sırasında kendini gizler.

## Derleme
```
dotnet publish src/Ada -c Release -r win-x64 --self-contained false -o publish
```
NativeAOT (daha az RAM, tek exe; Visual Studio "C++ ile masaüstü geliştirme" iş yükü gerekir):
```
dotnet publish src/Ada -c Release -r win-x64 -p:Aot=true -o publish-aot
```

## Test
```
dotnet test tests/Ada.Tests
Ada.exe --snap <klasör>      # her durumu ekran dışında PNG'ye çizer (gerçek veriye dokunmaz)
```
`ADA_DEBUG=1` ortam değişkeniyle saniyelik kare/durum bilgisi `%AppData%\Ada\ada.log`'a yazılır.

## Mimari
Tek layered Win32 penceresi + Direct2D (yazılım) → UpdateLayeredWindow. Animasyon varken vsync'li
döngü, yokken tamamen uyur. Ayrıntılar: `docs/2026-10-13-ada-design.md`.
