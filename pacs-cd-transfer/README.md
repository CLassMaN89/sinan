# PACS CD Transfer

Hastane PACS'ine dışarıdan gelen CD'lerden görüntü aktaran ve PACS'ten hasta
sorgulayıp geri çeken masaüstü uygulaması. `pacs-cd-design.html` mockup'ının
gerçek C# uygulaması.

## Mimari

- **`src/PacsCdTransfer.Core`** — platform bağımsız (net8.0) iş mantığı:
  gerçek DICOM ağ işlemleri ([fo-dicom](https://github.com/fo-dicom/fo-dicom)
  ile C-ECHO / C-FIND / C-MOVE / C-GET / C-STORE), CD/DICOMDIR okuma, TC
  Kimlik No doğrulama (gerçek checksum algoritması), ayar/kullanıcı
  saklama, gönderim orkestrasyonu.
- **`src/PacsCdTransfer.App`** — WPF arayüz (yalnızca Windows'ta derlenir —
  bkz. aşağıdaki not), Core'daki servisleri kullanır.
- **`test/PacsCdTransfer.Core.Tests`** — 18 test, hepsi gerçek TCP üzerinden
  DICOM protokolü konuşan bir SCP'ye karşı çalışır (mock değil).

## Neden test PACS olarak Orthanc değil de kendi SCP'miz?

Bu ortamda Docker Hub'ın CDN'i organizasyon ağ politikası tarafından
engellendiği için Orthanc imajı çekilemedi. Bunun yerine
`test/PacsCdTransfer.Core.Tests/TestScp.cs` fo-dicom'un kendi sunucu
altyapısıyla gerçek bir DICOM SCP'si başlatıyor ve testler gerçek TCP
soketleri üzerinden çalışıyor — PDU müzakeresi, association, C-ECHO,
C-FIND, C-STORE ve **gerçek bir C-MOVE geri döngüsü** (uzak SCP, hedef AE'ye
yeni bir alt-association açıp C-STORE ile görüntüleri gönderiyor — tam
olarak gerçek bir hastane PACS'inin yapacağı şey) dahil. Bu, mock'larla
kıyaslanamayacak kadar güçlü bir doğrulama; ama **gerçek bir hastane
PACS'ine karşı** (vendor'a özgü davranış farklılıkları olabilir) henüz test
edilmedi — canlıya geçmeden önce hedef PACS'e karşı bir C-ECHO/C-FIND/C-MOVE
denemesi yapılması önerilir.

## Önemli kısıt: WPF yalnızca Windows'ta derlenir

Microsoft, WPF/WinForms derleme araçlarını (Windows Desktop SDK) Linux'a
dağıtmıyor — bu, geliştirme ortamının bir sınırlaması değil, platformun
kendisinin sınırlaması. Bu yüzden:

- `PacsCdTransfer.Core` ve testleri bu ortamda (Linux) tam olarak derlenip
  **gerçek ağ testleriyle** doğrulandı.
- `PacsCdTransfer.App` (WPF arayüz) kodu eksiksiz yazıldı ama yalnızca
  Windows'ta derlenebiliyor — bu yüzden **GitHub Actions** (`.github/workflows/pacs-cd-transfer-build.yml`)
  `windows-latest` runner'da otomatik olarak derliyor, test ediyor ve
  **kurulum gerektirmeyen, taşınabilir, tek dosyalık `PacsCdTransfer.exe`**
  üretip Actions sekmesinde artifact olarak sunuyor.

### .exe'yi nasıl alırım?

1. GitHub'da bu depoda **Actions** sekmesine gidin.
2. "PACS CD Transfer — build, test, publish" iş akışının son (yeşil) çalışmasını açın.
3. **Artifacts** bölümünden `PacsCdTransfer-portable-win-x64` dosyasını indirin.
4. Zip'i açın, `PacsCdTransfer.exe`'yi istediğiniz klasöre (USB, ağ paylaşımı,
   masaüstü) kopyalayın ve çalıştırın — .NET kurulumu, kurulum sihirbazı,
   yönetici izni gerekmez. Bu hem "exe" hem "portable" isteğinizi tek dosyada karşılıyor.

İlk girişte kullanıcı adı `admin`, şifre `admin` (mockup'taki gibi) —
sonrasında Ayarlar > Kullanıcılar & Yetkiler'den değiştirin.

## Yerel geliştirme

```bash
# Core + testler her platformda çalışır:
dotnet test test/PacsCdTransfer.Core.Tests/PacsCdTransfer.Core.Tests.csproj

# WPF uygulaması yalnızca Windows'ta:
dotnet run --project src/PacsCdTransfer.App/PacsCdTransfer.App.csproj

# Taşınabilir, kurulum gerektirmeyen tek-dosya exe (Windows'ta):
dotnet publish src/PacsCdTransfer.App/PacsCdTransfer.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Şu ana kadar tam çalışır durumda olanlar

- CD/DVD sürücü algılama ve DICOMDIR + dosya taraması ile hasta/seri okuma.
- TC Kimlik No format + checksum doğrulaması (gönderim öncesi engelleme).
- C-ECHO bağlantı testi, C-FIND sorgu, C-MOVE / C-GET ile PACS'ten çekme,
  C-STORE ile PACS'e gönderme — hepsi gerçek ağ testleriyle doğrulandı.
- Ayarlar: yerel AE/port, birden fazla gönderim hedefi / sorgu kaynağı
  (varsayılan seçilebilir, C-MOVE/C-GET seçimi), kullanıcı yönetimi
  (şifre hash'lenmiş, admin/kullanıcı yetki ayrımı), bağlantı günlüğü.
- Ayarlar JSON olarak exe'nin yanında saklanır (`config.json`) — taşınabilir
  build, config'i kendisiyle taşır.

## Bilinen eksikler / sonraki adımlar

- WPF arayüzü mockup'ın (`pacs-cd-design.html`) görsel tasarımını (okyanus
  mavisi tema, worklist kartları, toast bildirimleri vb.) henüz birebir
  yansıtmıyor — işlevsel bir ilk sürüm. Görsel ince ayar, artık test edilmiş
  ve çalışan bir backend üzerine hızlıca eklenebilir.
- Yerel AE/port ayarı değiştirildiğinde DICOM sunucusunun yeniden
  başlatılması gerekiyor (şu an uygulama yeniden başlatılmalı).
- Gerçek hastane PACS'ine karşı canlı bir C-MOVE/C-GET denemesi henüz
  yapılmadı (yukarıdaki nota bakın).
