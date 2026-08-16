╔═══════════════════════════════════════════════════════════════════════╗
║              PACS CD TRANSFER - Web Application                      ║
║                  Localhost Installation Guide                        ║
╚═══════════════════════════════════════════════════════════════════════╝

📋 GEREKSINIMLER:
═════════════════

✓ Windows 7 SP1 veya daha yeni
✓ .NET 8.0 Runtime (otomatik dahil - exe dosyasında)
✓ İnternet Tarayıcı (Chrome, Firefox, Edge, Safari)

NOT: .NET Runtime'a ihtiyacınız YOK, çünkü "self-contained" 
     executable oluşturduk. Tüm gerekli dosyalar burada var.


🚀 BAŞLATMA:
════════════

SEÇENEK 1 - Otomatik (Basit):
  1. START.bat dosyasına çift tıklayın
  2. Uygulama otomatik olarak başlayacak
  3. Tarayıcıda http://localhost:5062 açılacak
  4. Giriş yapın (admin / admin)

SEÇENEK 2 - Manual:
  1. Komut Prompt (CMD) açın
  2. Bu klasöre gidin:
     cd C:\path\to\this\folder
  3. Şunu yazın:
     PacsCdTransfer.Web.exe
  4. Tarayıcıda açın: http://localhost:5062


🔐 GİRİŞ BİLGİSİ:
═════════════════

Kullanıcı: admin
Şifre: admin

(İlk login sonrası yeni kullanıcı ekleyebilirsiniz)


📊 UYGULAMADA NELER VAR:
═════════════════════════

✓ DICOM CD'den okuma
✓ PACS Sunucusu ile bağlantı (C-FIND, C-MOVE, C-GET, C-STORE)
✓ Hasta verisi aktarım
✓ Kullanıcı yönetimi
✓ İşlem günlüğü
✓ Ayarlar (port, AE Title, vb.)
✓ Bağlantı testi
✓ Transfer geçmişi


⚙️ AYARLAR:
═══════════

Web Port: 5062 (http://localhost:5062)
DICOM Port: 11112 (ayarlardan değiştirilebilir)
Depolama Klasörü: PacsCD_Temp (bu klasörün içinde)
Config Dosyası: AppSettings.json


🆘 SORUN ÇÖZME:
═══════════════

Problem: "Port 5062 zaten kullanımda"
Çözüm: 
  - START.bat dosyasını Notepad'le aç
  - "http://localhost:5062" olan kısmı değiştir
    (örn: http://localhost:5063)
  - Kaydet ve yeniden başlat

Problem: Uygulama başlamıyor
Çözüm:
  - Tüm dosyaların aynı klasörde olduğunu kontrol et
  - Windows Defender/Antivirus'un engellemiş olabilir
  - Uygulamaya izin ver

Problem: DICOM port'unda hata
Çözüm:
  - Ayarlar sekmesine git
  - Yerel Port'unu 11113 yap
  - Kaydet ve uygulamayı yeniden başlat


📁 DOSYA YAPISI:
═════════════════

PacsCdTransfer.Web.exe          → Ana uygulama
START.bat                       → Başlangıç dosyası
README.txt                      → Bu dosya
wwwroot/                        → Web dosyaları (HTML, CSS, JS)
  └── app.html                  → Web arayüzü
*.dll                           → .NET bileşenleri
AppSettings.json                → Konfigürasyon


🔄 GÜNCELLEME:
═══════════════

Yeni versiyonu indirip, bu klasördeki tüm dosyaları değiştirebilirsin.
Ayarlarınız AppSettings.json'a kaydedilmiştir - kopyala ve yapıştır
işleminden sonra geri yüklü olacaklardır.


🆘 DESTEK:
═══════════

Sorun yaşarsan:
1. START.bat'i açık tutup hata mesajını oku
2. Windows Firewall'da izin ver:
   - Settings > Firewall > Allow an app
   - PacsCdTransfer.Web'i ekle
3. DICOM port'unu kontrol et (varsayılan: 11112)


═════════════════════════════════════════════════════════════════════════

Tüm dosyaların aynı klasörde olduğundan emin ol!

Uygulamayı kullanabilirsin. Sorunda bize haber ver.

═════════════════════════════════════════════════════════════════════════
