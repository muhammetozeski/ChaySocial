# Ölçümler

Bu projede sayı üreten hiçbir karar tahminle verilmedi. Aşağıdakilerin hepsi çalıştırılıp
okundu; hangi ortamda ölçüldüğü de yazıyor, çünkü tarayıcı ile masaüstü arasındaki fark bu
projede kararı değiştirecek kadar büyük.

| Ne | Sonuç | Nerede |
|---|---|---|
| ChaCha20-Poly1305 / AES-256-GCM | 128,9 / 58,8 MB/s — **2,19×** | Masaüstü, BouncyCastle, 1 MB blok |
| Hibrit imza boyut maliyeti | +64 B (**%1,9**) | Gerçek imza |
| Hibrit KEM boyut maliyeti | +32 B (**%2,9**) | Gerçek kapsülleme |
| Argon2id, tek deneme | 24 ms | Masaüstü, 8 MiB |
| Argon2id, tek deneme | ~1,6 s (**~60×** yavaş) | Tarayıcı (WASM), 8 MiB |
| Argon2id, deneme hızı | **1,07 deneme/s** | Tarayıcı, 1 MiB |
| Argon2id, deneme hızı | ~400 deneme/s | Masaüstü, 1 MiB |
| 11 bit zorluk | **32 dakika** | Tarayıcı |
| 9 bit zorluk (seçilen) | 2–3 dakika | Tarayıcı |
| Ön plan / arka plan sekmesi | **2,86 / 0,42 deneme/s** | Tarayıcı |
| İzinden sonra bir gönderi | **13 ms** | Uçtan uca |
| Proof-of-work'lü her yazma (atıldı) | ~1,4 s | Uçtan uca |
| Sunucu tarafı proof doğrulaması | 10,6 ms | Sunucu |
| `.wall-page`, 375 px ekranda | **403 px** (14 px taşma) | Tarayıcı |
| `.app-page` yüksekliği / pencere | 790 px / 700 px | Tarayıcı |
| Cron emniyet ağı | **~85 beklenen ateşleme, 2 gerçekleşen** | Harness |
| 3 saatlik boşta kalma | **0 ateşleme** | Harness |
| Emoji fontu (gönderilmedi) | 24 MB | Kaynaklar |

---

## Şifre seçimi

```
BouncyCastle ChaCha20-Poly1305 :   128,9 MB/s
BouncyCastle AES-256-GCM       :    58,8 MB/s
ratio                          :    2,19x
```

İkisi de BouncyCastle'ın yazılım implementasyonu, aynı makinede, 1 MB blok. Donanım
hızlandırması olmadan ChaCha20 iki katından hızlı. Masaüstünde `AesGcm.IsSupported` **True**
çıkıyor, yani orada .NET'in hızlandırmalı sınıfı var — ama tarayıcıda yok, ve kripto zaten
tarayıcıda çalışmak zorunda.

Güvenlik seviyesi değişmiyor: iki şifrede de anahtar 256 bit, doğrulama etiketi 128 bit, ikisi
de TLS 1.3'ün zorunlu paketleri arasında.

## Hibrit maliyeti

| | Sadece PQ | Hibrit | Fark |
|---|---|---|---|
| İmza public key | 1952 B | 1984 B | +32 B (%1,6) |
| İmza | 3309 B | 3373 B | +64 B (%1,9) |
| KEM public key | 1184 B | 1216 B | +32 B (%2,7) |
| Kapsülleme | 1088 B | 1120 B | +32 B (%2,9) |

Klasik yarı bu fiyata duruyor çünkü ML-KEM ve ML-DSA 2024'te standartlaştı, kriptanaliz
geçmişleri kısa. Aynı NIST yarışmasında ileri tura kalmış SIKE ve Rainbow, "kuantum dirençli"
etiketiyle gelip 2022'de **klasik** bilgisayarla düştü — SIKE tek çekirdekte bir saatte.

## Proof-of-work zorluğu

Her bit süreyi ikiye katlıyor, o yüzden zorluk tahminle seçilemez:

```
1,07 deneme/saniye  (tarayıcı, 1 MiB Argon2id)
11 bit ≈ 2048 deneme ≈ 32 dakika     -> kağıt üzerinde makul görünüyordu
 9 bit ≈  512 deneme ≈  2-3 dakika   -> seçilen
```

Arka plan sekmesinde tarayıcı işi kısıyor: **0,42** deneme/s'ye karşı ön planda **2,86**. Bu
yüzden arayüzde "başka bir şey yap" demek yanlış — kullanıcıya kendi beklemesini uzattırıyor.
`await Task.Yield()` dörtlü gruplar halinde çağrılarak bu ceza grup başına bir kez ödeniyor.

Masaüstü .NET aynı Argon2id'yi tarayıcıdan ~400 kat hızlı çalıştırıyor (konsolda 1013 deneme
2,5 saniye). Yani proof-of-work, tarayıcıdaki gerçek kullanıcıyı native kod çalıştıran
saldırgandan çok daha fazla yoruyor; bellek-sertliği bu farkı daraltır ama kapatmaz.

## Yerleşim

İki yerleşim hatası da yalnızca ölçümle görüldü, gözle değil:

```
pageH: 790   innerHeight: 700     -> .app-page bir gezinme çubuğu (90px) kadar uzun
.wall-page: 403px   ekran: 375px  -> her kartın 14px'i sağdan taşıyor
```

İkincisi ekran görüntüsünde bile zor fark ediliyordu, çünkü `overflow-x: hidden` taşmayı
gizliyor ve `scrollWidth` temiz görünüyor. Gerçek ölçü elemanın kendi genişliği.

## Cron emniyet ağı

| | |
|---|---|
| Periyot | 5 dakika |
| Beklenen ateşleme | ~85 |
| Kuyruğa düşen | **2** (18:57 ve 19:54, ikisi de kullanıcı aktifken) |
| 22:43 → 01:46 arası (~3 saat boşta) | **0** |

Aracın kendi belgesi iki şey söylüyor: *"Jobs only fire while the REPL is idle"* ve `durable`
parametresi için *"Has no effect — durable persistence is not available."* Yani zamanlayıcı
oturumun belleğinde yaşıyor; oturum askıya alınırsa duruyor, ölürse ölüyor. Bu kurulumla cron
bir emniyet ağı değil.
