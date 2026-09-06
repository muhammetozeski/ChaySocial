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
| Seçilmiş adres araması | **5,0 aday/s** | Tarayıcı |
| 1 / 2 / 3 harf beklenen süre | ~6 s / ~3,4 dk / ~1,8 saat | Tarayıcı, 5,0 aday/s'den |
| İzinden sonra bir gönderi | **13 ms** | Uçtan uca |
| Proof-of-work'lü her yazma (atıldı) | ~1,4 s | Uçtan uca |
| Sunucu tarafı proof doğrulaması | 10,6 ms | Sunucu |
| `.wall-page`, 375 px ekranda | **403 px** (14 px taşma) | Tarayıcı |
| `.app-page` yüksekliği / pencere | 790 px / 700 px | Tarayıcı |
| Cron emniyet ağı | **~85 beklenen ateşleme, 2 gerçekleşen** | Harness |
| 3 saatlik boşta kalma | **0 ateşleme** | Harness |
| Emoji fontu (gönderilmedi) | 24 MB | Kaynaklar |
| Bir satırı okunduğu hale indirme | 3,49 µs | Masaüstü, 84 karakter |
| Tam içerik hükmü | **5,71 µs** | Masaüstü, 84 karakter |

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

## Seçilmiş adres kaç harfe kadar

Karşılama ekranındaki "adresim şununla başlasın" alanının kaç harf kabul edeceği tahminle değil,
tarayıcıda sayaç izlenerek belirlendi. İki harf istendi, ilerleme satırındaki sayı 20 saniye arayla
okundu:

```
48 aday   ->  20 saniye sonra  ->  148 aday
(148 - 48) / 20 = 5,0 aday/saniye   (tarayıcı, WASM, ön plan sekmesi)
```

Kısayolu yok, olması da istenmiyor: her aday tam bir ML-DSA-65 ve ML-KEM-768 anahtar üretimi,
çünkü adres iki public key'e birden bağlanıyor. Saldırganın başkasının adresini öğütmek için
ödeyeceği bedel de tam olarak bu.

| Harf | Beklenen deneme | Beklenen süre |
|---|---|---|
| 1 | 32 | ~6 saniye |
| 2 | 1024 | ~3,4 dakika |
| 3 | 32.768 | ~1,8 saat |

`ChosenLettersMaximumLength = 2` bu tablodan geliyor. Üç harf, kimsenin başında bekleyeceği bir
süre değil.

Ölçümdeki arama 148'den sonra bir dakikayı bulmadan bitti — beklenenin altında, çünkü 1024
ortalama, garanti değil. Sonuç `chayqqebpi…chiyac`: istenen iki harf yerinde.

## Yerleşim

İki yerleşim hatası da yalnızca ölçümle görüldü, gözle değil:

```
pageH: 790   innerHeight: 700     -> .app-page bir gezinme çubuğu (90px) kadar uzun
.wall-page: 403px   ekran: 375px  -> her kartın 14px'i sağdan taşıyor
```

İkincisi ekran görüntüsünde bile zor fark ediliyordu, çünkü `overflow-x: hidden` taşmayı
gizliyor ve `scrollWidth` temiz görünüyor. Gerçek ölçü elemanın kendi genişliği.

## İçerik hükmü

Bir gönderiyi okundu­ğu hale indirmek 3,49 µs, kategori ve bant dahil tam hüküm 5,71 µs — masaüstü .NET'te,
84 karakterlik bir satır için, 200.000 tekrarın ortalaması.

Hesap her çizimde değil, **metin değiştiğinde** yapılıyor: `WrittenLine` ve `WritingMirror` sonucu tuttukları
metinle birlikte saklıyor. Yani bir akış kaydırılırken hiçbir şey yeniden hükme varılmıyor.

Tarayıcı süresi ayrıca ölçülmedi; burada yazan rakam masaüstü rakamıdır. Yirmi gönderilik bir akış gözle
görülür gecikme olmadan çiziliyor, ama bu gözlem, ölçüm değil.

Prob sonuçları: kaçamak testleri **20/20**, hüküm testleri **29/29**. Kaçamak testleri aynı kelimenin on beş
farklı yazılışını (aksan, Kiril, Yunan, tam genişlik, leetspeak, basılı tutulan tuş, sıfır genişlikli karakter,
yumuşak tire, boşlukla ve noktayla ayırma) tek forma indiriyor.

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

## Üslup aynası — hangi eşik, neden

Ayna, beste ekranındaki taslağı bu cihazın taşıdığı hesapların yayınlanmış yazılarıyla karşılaştırıyor. İki
sayısı var ve ikisi de ölçüldü, seçilmedi.

Ölçüm kurulumu: uygulamanın kendi yayınlama yolundan beş hesaba gerçek yazı gövdeleri yazıldı — ikisi aynı
elden çıkmış ölçülü bir ses, biri kısa ve emojili, biri düz, biri de zaten orada duran uzun yazı. Sonra her
gövdeden o uzunlukta pencereler kesilip her hesabın altında yazılıyormuş gibi denendi.

Metrenin ayırdığı görülüyor: aynı elden çıkan iki gövde **0,9651**; diğer bütün çiftler **0,78–0,90**.

| Taslak uzunluğu | Eşik | Yanlış alarm | Yakalama | Doğru hesabı adlandırma |
|---|---|---|---|---|
| 140 | 0,03 | %4,8 | %66,7 | %75,0 |
| 180 | 0,00 | %20,0 | %95,0 | — |
| **180** | **0,03** | **%0,0** (0/15) | **%70,0** | **%88,1** |
| 240 | 0,03 | %0,0 | %72,7 | %93,8 |
| 320 | 0,03 | %0,0 | %86,1 | %90,3 |

"Yanlış alarm" = kendi sesiyle kendi hesabında yazan birinde başka bir hesabın öne geçmesi. "Yakalama" =
başka bir hesabın altında yazarken kendi sesinin bulunması. "Doğru hesabı adlandırma" = tetiklendiğinde
adlandırdığı hesabın gerçekten o sesin sahibi olması.

Seçilen değerler: `ShortestJudgeableDraft = 180` karakter, `ClosenessLeadWorthMentioning = 0,03`.

140 karakter derdi çözmüyordu: satır dört seferde bir yanlış hesabı adlandırıyordu. Eşiksiz çalıştırmak da
çözmüyordu: kendi hesabında kendi sesiyle yazanların beşte birinde tetikleniyordu. 180'in bedeli 140–179
arasındaki taslaklarda susmak; bir koruma satırı için yanlış tarafta olmanın doğru tarafı bu, çünkü boşuna
öten satır öğrenilip atlanan satırdır.

Örneklem küçük: 180'de yanlış alarm ölçümü 15 denemeye, 240'ta 11 denemeye dayanıyor. %0 rakamı "hiç olmaz"
demek değil, "bu on beş denemede olmadı" demek.

## Çizilmiş yüz — profil ne kadar şişiyor

Yüz, bir blob olarak değil, profilin **içinde** duruyor. Sebep: bir profil zaten bir ekranda yazar başına
bir kez okunuyor; yüz başına ikinci bir çekim aynı ekranda ikinci kez ödenirdi. Bunun bedeli, profil
belgesinin büyümesi — ve o yüzden iki tavan var.

Ölçümler (`JsonSerializerDefaults.Web`, deponun kullandığı bicim):

| | bayt |
|---|---|
| Çizimsiz profil (iki post-kuantum açık anahtar dahil) | **4.583** |
| Tarayıcıda gerçekten çizilen bir yüz (8 darbe, 24 nokta) | +1.216 → **5.799** |
| Tahtanın 3 px örneklemesiyle çizilmiş dolgun bir yüz (5 darbe, 228 nokta) | +4.219 → **8.802** |

Tavan adaylarının izin verdiği en kötü durum:

| Tavan | Sayfa | Profil |
|---|---|---|
| 20 darbe / 300 nokta | 6.312 | 10.895 (10,6 KiB) |
| **30 darbe / 400 nokta** | **8.352** | **12.935 (12,6 KiB)** |
| 30 darbe / 500 nokta | 9.972 | 14.555 (14,2 KiB) |
| 40 darbe / 600 nokta | 12.552 | 17.135 (16,7 KiB) |

Seçilen: `MaximumAvatarSketchStrokes = 30`, `MaximumAvatarSketchPointsAltogether = 400`. 300 nokta fazla
dardı — tahtanın kendi 3 piksellik örneklemesiyle çizilen sıradan bir yüz zaten 228 nokta, yani biraz
daha ayrıntılı bir yüz reddedilirdi. 400, o yüzü ve üstüne biraz ayrıntıyı alıyor, resme dönüşeni almıyor.

Profilin çizimsiz hâlinin zaten 4,5 KiB olduğunu not etmek gerekiyor: bunun neredeyse tamamı ML-DSA-65 ve
ML-KEM-768 açık anahtarları. Yüz, küçük olan bir belgeyi büyütmüyor; zaten büyük olanı en kötü ihtimalle
üçe katlıyor.
