# Kalıcı kurallar

Bu projede geçerli olan, proje sahibinin koyduğu kurallar. Alıntılar birebir; hiçbiri
yorumlanmadan yazıldı. Bir kural burada yazıyorsa tartışmaya açık değildir.

| # | Kural | Kaynak |
|---|---|---|
| 1 | JavaScript ve C#/Blazor dışı dil yasak — JS interop string'i de JavaScript yazmaktır | 3 Eylül |
| 2 | CSS serbest, ama Blazor üzerinden ve CSS değişkeni değil C# sabiti | 3 Eylül |
| 3 | Magic number yasak, CSS'te bile | 3 Eylül |
| 4 | `MainProject` dışına olabildiğince çıkma | 3 Eylül |
| 5 | Şablondaki kod yazım tarzına uy | 3 Eylül |
| 6 | Hızlı derleme denetimi `core compile` ile | 3 Eylül |
| 7 | Her minik adımda commit; commit başına tek konu | 4 Eylül |
| 8 | Proje içi her şey İngilizce | uygulamada |
| 9 | Kimsenin secret'ı sunucuya gitmez | 3 Eylül |
| 10 | Alt ajanı gerekmedikçe açma | 4 Eylül |
| 11 | Gerçek ortamda çalışsın — çalışmıyorsa yapamamışsındır | 4 Eylül |
| 12 | Otonom döngüde kullanıcıya soru sorma | `/İyiDöngü` |
| 13 | Sansür ve moderasyona en son bak | 4 Eylül |
| 14 | Proof-of-work: hesap bedava, yazma izni bir kez | 5 Eylül |

---

## 1. Dil yasağı

> javascript veya c# ve blazor dışı diller yasak. python falan yasak.

Bu yasak JS **dosyası** yazmakla sınırlı değil. Şu satır da yasaktır:

```csharp
await jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);   // YASAK
```

Çünkü o string bir JavaScript çağrısıdır. Yasağın dışında kalan tek şey, JS'i kendi içinde
taşıyan ve dışarıya C# tipli bir yüzey veren paketlerdir:

```csharp
await localStorage.SetItemAsync(SeedStorageKey, seedText);   // Blazored.LocalStorage — serbest
```

Sınırın nerede olduğu bir yanlış anlamayla netleşti: yasak "tarayıcı API'sine hiç dokunma"
diye okunmuştu, oysa kastedilen "senin kodunda JavaScript geçmesin" idi.

> SANA JAVASCRİPT YAZMA DEDİM, BUNU SARMALAYAN BLAZORU KULLANMA DEMEDİM Kİ, ONA KALIRSAK
> BLAZOR ZATEN HER ŞEYİN SARMALAYICISI.

## 2. CSS, C# üzerinden

> css serbest ama blazor üzerinden ve css değişkenleri yerine c# üzerinden kullanılacak.

Her bileşen kendi `*Css.razor` dosyasını render eder, renkler ve ölçüler oraya render anında
C# değerinden yazılır. `var(--x)` yok. Bunun bedava getirdiği şey tema değişimi: palet bir C#
kaydı olduğu için bir sonraki render'da bütün uygulama yeniden renkleniyor.

## 3. Magic number yasağı

> css'te de olsan, magicNumber yasak.
> int 56px = 56 tarzı gerizekalıca şeyler de magic number sayılır.
> int ViewBoxWeight = 56 tarzı şeyler de magic number'dır.
> int UserProfileBannerWeightPx = 56 tarzı isimlendirme DOĞRU isimlendirmedir!

Ölçüt, ismin sayının **neyi** olduğunu söylemesi. `56` neyse odur; `ViewBoxWeight` hangi kutunun
neyi olduğunu söylemiyor; `UserProfileBannerWeightPx` söylüyor.

## 4. Proje sınırı

> MainProject dışına olabildiğince çıkma, tamam mı?

`MainProject` dışına ancak derleyici zorlarsa çıkılır. Bugüne kadar tek gerekçe ASP.NET Core
sunucu tipleri oldu (`WebApplication`, `MapGet`, `Results`) — bunlar `Microsoft.AspNetCore.App`
paylaşılan çerçevesinde ve `MainProject`'i MAUI Android başlığı da referans aldığı için oraya
konamıyor. Bunun dışında her şey `MainProject` içinde.

Bir zamanlar `Groundwork` adında ayrı bir jenerik proje vardı; silindi.

## 5. Kod tarzı

> şablonda benim kod yazım tarzıma uy.

Şablonun kendi tarzı esas alınır: kendi event sistemi (`MainEvents`, `readonly HashSet` üstünde),
kendi `Logger`'ı, sayfa hiyerarşisi, `*Css.razor` ayrımı. Bunların yanına ikinci bir sistem
kurulmaz — mevcut olan değiştirilir.

## 6. Derleme denetimi

> hızlı derleme test'leri için "core compile" kullan. tam test de yapabilirsin gerektiğinde.

```bash
dotnet build ChaySocial.Web/ChaySocial.Web.csproj -t:Compile
```

`-t:CoreCompile` tek başına referansları çözmüyor (bkz. `LESSONS.md`), bu yüzden hedef `Compile`.

## 7. Commit disiplini

> commit atmayı unutma her minik adımda.

Her commit tek bir konu taşır. Deneysel bir değişiklik ile sağlam bir düzeltme asla aynı
commit'te olmaz — yoksa kötü olan, iyisini kaybetmeden geri alınamaz.

## 8. Dil

Kod, yorum, XML belgeleri ve arayüz metinlerinin tamamı İngilizce. Tarih biçimlendirmesi dahil:
kültür verilmezse sayfanın ortasında dil değişiyor (bkz. `LESSONS.md`).

Bu dosya ve `LESSONS.md` istisnadır; ikisi de proje sahibinin kendi notları.

## 9. Sır sunucuya gitmez

> kimsenin secret'ı server'a iletilmeyecek, server proton mail'in yaptığı gibi şifreyi almadan
> kullanıcıyı çözecek.
> kullanıcı özel mesajları e2ee şifreli olacak, server hiçbir mesaj içeriğini bilmeyecek.

Bu kural bir mimari kısıt: tarayıcıda çalışan kod Blazor **WebAssembly** olmak zorunda, çünkü
Blazor Server'da bütün C# sunucuda çalışır ve tohum oraya gider.

Sunucu bir saldırgan gibi modellenmez. Sunucu düşman değil; sadece bilmesi gerekmeyen şeyi
bilmiyor.

> SUNUCU BİR HACKER DEĞİL. ANAHTARI İSTEMEYECEK SUNUCU.

## 10. Alt ajanlar

> alt ajanları çok gerekli değilse açma çünkü anlamsızca token harcatıyorsun onlara.

## 11. Gerçekten çalışması

> şuna önem ver: gerçek ortamda çalışsın. çalışmıyorsa yapamamışsındır. yerelde server + client
> ayrı ayrı düzgünce iletişim kurduğundan emin ol her özelliğin ve diske yazıp kalıcı da olmalı.
> arka planda senin yapman değil, guide kullanıcı o özelliği kullanabilecek mi? bu asıl önemli
> olan kısım.

Ölçüt "derleniyor" değil, hatta "test geçiyor" bile değil: **kullanıcı o özelliği arayüzden
kullanabiliyor mu**. Bir özellik, gerçek bir tarayıcıda gerçek bir sunucuya karşı, verisi diskte
kalacak şekilde denenmeden bitmiş sayılmaz.

## 12. Döngüde soru yok

Otonom döngü çalışırken kullanıcıya soru sorulmaz — örtük soru da yasaktır ("istersen yaparım",
"devam dersen" gibi turu kullanıcının cevabına bağlayan cümleler dahil). Belirsizlik durma
sebebi değildir: en makul ve güvenli yorum seçilir, sebebi tek satır yazılır, devam edilir.

Doğrulanamayan iş `Brainstorm/Deferred`'a notuyla park edilir, körlemesine shiplenmez.

## 13. Moderasyon sırası

> anonimlik güzel fakat anonimlikten doğacak olan "abusive" veya illegal paylaşımları önleme
> yöntemleri? kullanıcıları sıkmamaya ve anonimliliği öldürmemeye özen göstermeliyiz. bir insan
> birden fazla bir sürü hesap sahibi olabilmeli. sadece abusive ve aşırı rahatsız edici
> paylaşımlar engellenebilmeli.
> genel olarak diğer platformlardan daha serbest olalım. oto sansür insanlara hala özgür
> hissettirmeli. bunun detaylarını sonra konuşuruz, sansür ve moderasyona en son bak sen.

## 14. Proof-of-work

> BU BEKLEMEYİ PROOF OF WORK İLE YAPACAKSIN.

Bekleme, yapay bir sayaçla değil, cihazın gerçekten iş yapmasıyla üretilir.

| | |
|---|---|
| Hesap açmak | Bedava, anında |
| Okumak, takip etmek, chay ısmarlamak | Bedava, anında |
| **Yazma izni** | Ömürde bir kez, dakikalar süren proof-of-work |
| İzinden sonra her yazma | Anlık |

Mesaj başına proof-of-work bir kez denendi ve atıldı: uygulamayı kullanan kişinin telefonunu
her mesajda ~1,4 saniye yakıyordu, spam yapan ise bu bedeli ödemeye zaten razıydı. Pahalı
olması gereken şey bin tane yazan hesap açmaktır.
