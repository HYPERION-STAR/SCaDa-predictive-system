# SCaDa — Çalıştırma Rehberi (macOS)

> **Kapsam:** Bu repo projenin **veritabanı katmanıdır** (Kişi 4). Hub, model
> servisi ve UI ayrı makinelerde çalışır; buradan onlara Tailscale IP'leriyle
> bağlanılır. `hub/`, `ui/`, `predict_service.py` bu repoda **yoktur**.
>
> macOS + zsh varsayılır. Komutlar 2026-07-23'te bu makinede (Darwin 23.6,
> .NET 9.0.101, MySQL 8.4 Homebrew) çalıştırılıp doğrulanmıştır.
>
> Windows kurulumu için bu dosyanın git geçmişindeki önceki sürümüne bakın —
> orada Docker + PowerShell adımları vardır.

## Mimari

```
[Kişi 2'nin makinesi]                    [Kişi 1'in makinesi]
publisher ──MQTT──> hub API              model servisi
                    100.114.223.5:8000   100.123.105.25:8010
                    /api/scada/live_data /hub_state · /predict
                          │                     │
                          └──────────┬──────────┘
                                     ▼
                        [bu Mac] SCaDaClient (C#)
                                     │
                                     ▼
                        MySQL :3306 pipeline_digital_twin
                          ├── live_entity_current   (192 varlık, anlık)
                          ├── live_snapshot_history (ham gövde/tur)
                          ├── telemetry_readings    (~1822 satır/snapshot)
                          ├── predictions           (model çıktısı)
                          └── ai_respond            (/hub_state log'u)
                                     │
                          ┌──────────┴──────────┐
                          ▼                     ▼
                 yayın :9000 (pull)      HTTP POST (push)
                 karşı bilgisayar        karşı bilgisayara
                 çeker                   gönderilir
```

Tek bir `dotnet run` şunların hepsini birden çalıştırır:

| Servis | Kaynak | Hedef tablo | Periyot |
|--------|--------|-------------|---------|
| `LiveDataService` | hub `/api/scada/live_data` | `live_entity_current`, `live_snapshot_history`, `telemetry_readings` | 5 sn |
| `AiRespondService` | model `/hub_state` | `ai_respond` | 5 sn |
| `AiForwardService` | `ai_respond` (pending) | karşı bilgisayara HTTP POST | 5 sn |
| `AiServe` (yayın) | `ai_respond` | :9000 HTTP uçları — karşı bilgisayar çeker | istek üzerine |

---

## 0. Ön koşullar (tek seferlik)

```bash
# .NET 9 SDK
dotnet --version        # 9.0.101 ile doğrulandı

# MySQL 8.4 (şema utf8mb4_0900_ai_ci + JSON_TABLE + events gerektirir)
brew install mysql@8.4
brew services start mysql@8.4

# mysql istemcisini PATH'e ekle (Homebrew keg-only kurar)
echo 'export PATH="/opt/homebrew/opt/mysql@8.4/bin:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

> **Docker gerekmez.** Bu makinede MySQL doğrudan Homebrew servisi olarak çalışır;
> dokümanın eski sürümündeki `docker run` / `docker exec` komutları burada
> geçersizdir.

---

## 1. MySQL (Kişi 4)

```bash
# Ayakta mı?
brew services list | grep mysql        # "started" olmalı
mysqladmin ping -uroot -p'Botas1974.'  # "mysqld is alive"

# Şemayı SIRAYLA yükle (sıra zorunlu — view'lar öncekilere bağımlı)
cd ~/Desktop/scada_predictive_maintenance-DB
mysql -uroot -p'Botas1974.' pipeline_digital_twin < db/digital_twin_script.sql
mysql -uroot -p'Botas1974.' pipeline_digital_twin < db/live_data_database.sql
mysql -uroot -p'Botas1974.' pipeline_digital_twin < db/regions_topology.sql
mysql -uroot -p'Botas1974.' pipeline_digital_twin < db/ai_respond.sql
```

> **Yönetim:** başlat `brew services start mysql@8.4` · durdur
> `brew services stop mysql@8.4` · yeniden başlat `brew services restart mysql@8.4`.
> Veri `/opt/homebrew/var/mysql` altında kalıcıdır.
>
> **Güvenlik:** parola burada yerel geliştirme makinesi içindir. Gerçek dağıtımda
> `.env` kullanın, kaynağa/dokümana yazmayın.

---

## 2. Bağımlı servisler (başka makinelerde)

Bu Mac'te hiçbir şey başlatmanız gerekmez — sadece erişilebilir olmaları yeterli.
Tailscale bağlıysa:

```bash
# Hub (Kişi 2) — 192 varlık dönmeli
curl -s http://100.114.223.5:8000/api/scada/live_data | head -c 200

# Model servisi (Kişi 1) — 24 düğüm + 122 segment dönmeli
curl -s http://100.123.105.25:8010/hub_state | head -c 200
```

> **Dikkat — yol küçük harf:** hub `/api/scada/...` sunar. `/api/SCaDa/...`
> **404** döner (FastAPI yolları büyük/küçük harfe duyarlı). Kodda bir dönem
> `SCaDa` yazıyordu, düzeltildi.

Bu servisleri kendi makinenizde ayağa kaldırmanız gerekirse ilgili kişinin
reposundaki talimatları izleyin; MQTT broker'ı (`mosquitto -p 1883`) ve
publisher yalnızca hub'ı da siz çalıştırıyorsanız gerekir.

---

## 3. SCaDaClient — canlı veriyi çek ve DB'ye yaz

Repo **kökünden**:

```bash
cd ~/Desktop/scada_predictive_maintenance-DB

SCaDaApi__BaseUrl="http://100.114.223.5:8000" \
AiHub__BaseUrl="http://100.123.105.25:8010" \
Database__ConnectionString="Server=127.0.0.1;Port=3306;Database=pipeline_digital_twin;User ID=root;Password=Botas1974.;SslMode=Preferred" \
  dotnet run --project SCaDaClient/SCaDaClient.csproj -c Release
```

Beklenen log (her 5 sn):

```
[14:41:05] Canlı: 192 varlık (24 kompresör, 122 segment)
[DB OK] snapshot_id=1094, varlık=192, narrow_telemetri=1822, saklanan_snapshot=883, hedef=127.0.0.1:3306/pipeline_digital_twin
[AI OK] respond_id=37, düğüm=24, segment=122, kritik=6, sızıntı=0, gecikme=481ms
```

Yayın da bu komutla açılır (§6A). Ayakta olduğunu doğrulamak için ayrı terminalden:

```bash
curl http://127.0.0.1:9000/health
```

### Model tahminlerini de yazmak (`predictions` tablosu)

Varsayılan `UseModelService=false` — kompresörler kural tabanlı değerlendirilir ve
`predictions` boş kalır. Model servisini devreye almak için:

```bash
PredictionApi__BaseUrl="http://100.123.105.25:8010" \
PredictionApi__UseModelService="true" \
  # ... yukarıdaki diğer env'lerle birlikte
```

---

## 4. Doğrulama (ayrı terminalden)

```bash
mysql -uroot -p'Botas1974.' pipeline_digital_twin -e "
SELECT COUNT(*) snapshots, MAX(snapshot_id) son_id, MAX(captured_at) son_zaman
  FROM live_snapshot_history;
SELECT COUNT(*) telemetri FROM telemetry_readings;
SELECT COUNT(*) varlik, MAX(updated_at) son_guncelleme FROM live_entity_current;
SELECT send_status, COUNT(*) adet, MAX(respond_id) son FROM ai_respond GROUP BY send_status;"
```

Sağlıklıysa: `son_zaman` şu anki saate yakın, sayaçlar her turda artıyor
(`telemetry_readings` +1822/snapshot), `live_entity_current` 192 satır.

Sürekli izlemek için: `watch -n5 'mysql -uroot -p"Botas1974." ... '`
(`brew install watch`).

Modelin en kötü durumdaki kompresörleri:

```bash
mysql -uroot -p'Botas1974.' pipeline_digital_twin -e "
SELECT n.* FROM ai_respond r
JOIN JSON_TABLE(r.payload_json, '\$.nodes[*]' COLUMNS (
  id VARCHAR(50) PATH '\$.id', health DECIMAL(7,3) PATH '\$.health',
  rul DECIMAL(12,3) PATH '\$.rul', vibration DECIMAL(9,3) PATH '\$.vibration',
  bearing_temp DECIMAL(9,3) PATH '\$.bearingTemp')) AS n
WHERE r.respond_id = (SELECT MAX(respond_id) FROM ai_respond)
ORDER BY n.health LIMIT 10;"
```

---

## 5. ai_respond tablosu

`ai_respond`, model servisinin `/hub_state` yanıtlarının log tablosudur. Her poll
turu **tek satır**: gövde `payload_json` içinde olduğu gibi saklanır, yanına özet
sütunları (`critical_node_count`, `min_health`, `min_rul`, `leak_segment_count`,
`latency_ms`) yazılır. Gövde bir öncekiyle birebir aynıysa (`payload_sha256`
eşit) yeni satır **yazılmaz** — log şişmez.

---

## 6. Karşı bilgisayara veri verme

İki yol var; ikisi de aşağıdaki **aynı JSON zarfını** kullanır ve aynı anda açık
olabilirler.

```json
{
  "respond_id": 37,
  "fetched_at": "2026-07-23T15:25:20.464",
  "source_url": "http://100.123.105.25:8010/hub_state",
  "model_updated_at": "2026-07-23T15:25:12",
  "node_count": 24, "segment_count": 122,
  "critical_node_count": 6, "leak_segment_count": 0,
  "min_health": 3.800, "min_rul": 15.100,
  "payload": { "nodes": [...], "segments": [...], "updated_at": "..." }
}
```

`payload` modelin `/hub_state` gövdesinin aynısıdır; üstündeki alanlar
izlenebilirlik için DB'den eklenir.

### A) Yayın — karşı bilgisayar ÇEKER (pull) · varsayılan açık

SCaDaClient çalışırken :9000'de HTTP yayını yapar. **Karşı bilgisayara verilecek
adres:**

```
http://100.96.102.16:9000
```

(Bu Mac'in Tailscale IP'si — `tailscale ip -4` ile teyit edilir. Tailscale
kapalıysa aynı LAN'da yerel IP de kullanılabilir: `ipconfig getifaddr en0`.)

| Uç | Ne döner |
|----|----------|
| `GET /health` | Yayın ayakta mı, `latest_respond_id`, `row_count` |
| `GET /ai_respond/latest` | En son satır (tek nesne) |
| `GET /ai_respond?since=N&limit=M` | N'den **sonraki** satırlar + `next_since` imleci |

Karşı taraf sürekli veri istiyorsa `since` imlecini kullanmalı — böylece aynı
satırı iki kez almaz:

```python
import time, requests

BASE = "http://100.96.102.16:9000"
since = 0
while True:
    r = requests.get(f"{BASE}/ai_respond", params={"since": since, "limit": 100}, timeout=15)
    r.raise_for_status()
    data = r.json()
    for item in data["items"]:
        nodes = item["payload"]["nodes"]        # 24 kompresör
        segments = item["payload"]["segments"]  # 122 segment
        print(item["respond_id"], item["critical_node_count"], len(nodes), len(segments))
    since = data["next_since"]                  # imleci ilerlet
    time.sleep(5)
```

Sadece anlık durum yetiyorsa tek satır:

```bash
curl http://100.96.102.16:9000/ai_respond/latest
```

Yayın ayarları (`AiServe`):

```bash
AiServe__Enabled="true" \
AiServe__Port="9000" \
AiServe__BindAddress="0.0.0.0" \   # tüm arayüzler; 127.0.0.1 = sadece bu makine
AiServe__ApiKey="gizli-anahtar" \  # boşsa kimlik doğrulama yok
  # ... diğer env'lerle birlikte
```

> **Güvenlik:** `BindAddress=0.0.0.0` yayını tailnet'teki **herkese** açar ve
> gövde DB içeriğidir. Paylaşımlı bir tailnet'te `AiServe__ApiKey` verin; karşı
> taraf `X-Api-Key` başlığıyla istek atar. Anahtar ayarlıysa başlıksız istekler
> 401 döner.

### B) Gönderim — biz POST EDERİZ (push)

Karşı bilgisayarda hazır bir endpoint varsa hedef adresi verin:

```bash
AiForward__TargetUrl="http://KARSI_IP:PORT/ingest" \
AiForward__ApiKey="..." \        # karşı taraf istiyorsa; X-Api-Key başlığıyla gider
  # ... diğer env'lerle birlikte
```

`TargetUrl` boşsa gönderici hiç çalışmaz; satırlar `send_status='pending'` olarak
birikir ve **veri kaybı olmaz** (outbox deseni). Karşı bilgisayar kapalıyken de
aynısı geçerli: ağ dönünce satırlar sırayla ve eksiksiz gider, canlı veri toplama
etkilenmez. Bir satır `MaxAttempts` (varsayılan 5) denemede gitmezse `failed`
işaretlenir ve kuyruğu tıkamaz.

> **Karşı tarafa söylenecek:** yanıtta `Content-Length` başlığı bulunmalıdır.
> HTTP/1.1 kullanıp bu başlığı göndermeyen bir sunucuda client yanıtı bekler ve
> 15 sn sonra timeout'a düşer.

`failed` satırları tekrar denemek için:

```bash
mysql -uroot -p'Botas1974.' pipeline_digital_twin -e "
UPDATE ai_respond SET send_status='pending', send_error=NULL WHERE send_status='failed';"
```

---

## 7. UI (Kişi 3, WPF)

**macOS'ta çalışmaz.** UI `net9.0-windows` hedefli WPF uygulamasıdır; WPF yalnızca
Windows'ta çalışır ve projesi bu repoda değildir. UI'ı Kişi 3'ün Windows
makinesinde çalıştırın; oradan bu Mac'teki servislere Tailscale IP'siyle bağlanır.

---

## 8. Kapatma

```bash
# SCaDaClient (yayın :9000 dahil): terminalde Ctrl+C, veya:
pkill -f "dotnet run --project SCaDaClient"
pkill -f "SCaDaClient/bin/Release"

# 9000 gerçekten kapandı mı:
lsof -nP -iTCP:9000 -sTCP:LISTEN

# MySQL:
brew services stop mysql@8.4
```

---

## Sorun giderme

| Belirti | Neden / çözüm |
|---------|---------------|
| Client 404 alıyor, hiç veri gelmiyor | URL'de `/api/SCaDa/...` yazıyor. Doğrusu küçük harf `/api/scada/...`. Kodda düzeltildi; repo'da `botas`→`SCaDa` toplu yeniden adlandırmasından kalmıştı. |
| `zsh: command not found: docker` | Bu makinede Docker yok, gerekmiyor da. MySQL Homebrew servisi — `brew services list`. |
| `zsh: command not found: mysql` | Homebrew MySQL keg-only. PATH'e ekleyin (bkz. §0) veya tam yol: `/opt/homebrew/opt/mysql@8.4/bin/mysql`. |
| `Access denied for user 'root'@'localhost'` | Parola `Botas1974.` — dokümanın eski sürümündeki `SCaDa1974.` Docker container'ı içindi, bu kurulumda geçersiz. |
| `MySqlException: Unable to connect` | `brew services start mysql@8.4`. |
| `predictions` boş | `PredictionApi__UseModelService=true` olmalı **ve** model servisi :8010 açık olmalı. |
| `ai_respond` boş | Model servisine erişim yok: `curl http://100.123.105.25:8010/hub_state` ile doğrulayın (Tailscale bağlı mı?). |
| `ai_respond` dolu ama hep `pending` | `AiForward__TargetUrl` ayarlanmamış. Ayarlanmışsa `send_error` sütununa bakın. |
| Gönderimde 15 sn timeout | Karşı taraf yanıtta `Content-Length` göndermiyor (bkz. §6B). |
| Karşı bilgisayar yayına bağlanamıyor | Sırayla: client çalışıyor mu (`lsof -nP -iTCP:9000 -sTCP:LISTEN`), `BindAddress=0.0.0.0` mı (127.0.0.1 ise dışarı kapalıdır), Tailscale iki tarafta da bağlı mı (`tailscale status`). |
| Yayın 401 dönüyor | `AiServe__ApiKey` ayarlı — karşı taraf `X-Api-Key` başlığını göndermeli. |
| Karşı taraf aynı satırı tekrar tekrar alıyor | `/ai_respond/latest` yerine `/ai_respond?since=N` kullanmalı ve her turda `next_since` ile imleci ilerletmeli (bkz. §6A). |
| Yayın açılmıyor, port hatası | 9000 başka bir süreçte. `AiServe__Port` ile değiştirin. |
| `/hub_state` hep aynı değer | Kişi 1'in servisi dosya fallback'inde — canlı hub'a bağlı değil. `SkipUnchanged=true` sayesinde `ai_respond`'a tekrar satır yazılmaz. |
| `dotnet build` "file locked" | Çalışan client'ı kapatın (`pkill -f SCaDaClient`), tekrar derleyin. |
