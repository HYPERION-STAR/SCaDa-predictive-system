# SCaDa — Uçtan Uca Çalıştırma Rehberi

> Bu rehber, dört ekip parçasını (hub, model, veritabanı, UI) **tek makinede**
> baştan sona ayağa kaldırma sırasını içerir. Tüm komutlar 2026-07-23'te bu
> makinede çalıştırılıp doğrulanmıştır. Windows + PowerShell/Git Bash varsayılır.

## Mimari

```
publisher (SCaDa_engineV1) ──MQTT──> broker :1883 ──> hub API :8000 (/live_data)
                                                          │
                          ┌───────────────────────────────┼───────────────────────────┐
                          ▼                               ▼                             ▼
                 SCaDaClient (C#)                predict_service :8010          UI (WPF, Canlı mod)
                   │  /live_data okur              /hub_state (24 model düğümü)   /hub_state'ten
                   │  /predict çağırır             /predict (kompresör tahmini)   model sağlık/RUL
                   ▼                                                              haritada gösterir
                 MySQL :3306
                   ├── live_entity_current (192)
                   ├── telemetry_readings  (~1822/snapshot, narrow)
                   └── predictions         (model çıktısı, FK: entities)
```

---

## 0. Ön koşullar (tek seferlik)

```bash
# .NET 9 SDK (net9.0-windows WPF için Desktop runtime dahil) + dotnet 10 de olur
dotnet --version

# Python bağımlılıkları (predict_service + hub + eğitim)
python -m pip install -r requirements.txt

# Docker Desktop (MySQL container için) — kurulu değilse: winget install Docker.DockerDesktop
docker --version
```

---

## 1. MySQL (veritabanı katmanı — Kişi 4)

**Docker Desktop çalışıyor olmalı** (`docker ps` hatasızsa hazır).

```bash
# 1a. MySQL 8.4 container (şema utf8mb4_0900_ai_ci + JSON_TABLE + events gerektirir)
docker run -d --name SCaDa-mysql \
  -e MYSQL_ROOT_PASSWORD='SCaDa1974.' \
  -e MYSQL_DATABASE=pipeline_digital_twin \
  -p 3306:3306 mysql:8.4

# 1b. Hazır olmasını bekle (~5-30 sn)
docker exec SCaDa-mysql mysqladmin ping -uroot -p'SCaDa1974.'   # "mysqld is alive"

# 1c. Şemayı SIRAYLA yükle (sıra zorunlu — view'lar öncekilere bağımlı)
docker exec -i SCaDa-mysql mysql -uroot -p'SCaDa1974.' pipeline_digital_twin < db/digital_twin_script.sql
docker exec -i SCaDa-mysql mysql -uroot -p'SCaDa1974.' pipeline_digital_twin < db/live_data_database.sql
docker exec -i SCaDa-mysql mysql -uroot -p'SCaDa1974.' pipeline_digital_twin < db/regions_topology.sql
```

> **Yönetim:** durdur `docker stop SCaDa-mysql` · başlat `docker start SCaDa-mysql`
> (veri kalıcı) · sil `docker rm -f SCaDa-mysql`.
> **Güvenlik:** parola burada yerel test container'ı içindir. Gerçek dağıtımda
> `.env` kullanın, kaynağa/dokümana yazmayın.

---

## 2. MQTT broker (:1883)

**Tercih A — mosquitto** (Kişi 2'nin gerçek kurulumu):
```bash
mosquitto -p 1883
```

**Tercih B — mosquitto yoksa, saf Python broker** (`pip install amqtt`):
```python
# broker.py
import asyncio
from amqtt.broker import Broker
cfg = {"listeners": {"default": {"type": "tcp", "bind": "127.0.0.1:1883"}},
       "sys_interval": 0, "auth": {"allow-anonymous": True}}
async def main():
    b = Broker(cfg); await b.start()
    print("broker 1883 hazir"); await asyncio.Event().wait()
asyncio.run(main())
```
```bash
python broker.py
```

---

## 3. Hub — veri akışı (Kişi 2)

`hub/` dizininden çalıştırın (script'ler göreli yol kullanır):

```bash
cd hub

# 3a. Hub API (:8000) — MQTT'ye abone olur, /live_data servis eder
python -m uvicorn app.main:app --host 127.0.0.1 --port 8000

# 3b. AYRI terminalde: telemetri yayıncısı (broker'a basar, degradation.py'yi
#     repo kökünden import eder — sys.path shim'i sayesinde)
python SCaDa_engineV1.py
```

Doğrulama: `curl http://127.0.0.1:8000/api/SCaDa/live_data` → 192 varlık.

---

## 4. Model servisi (Kişi 1) — :8010

Repo **kökünden**:
```bash
# SCaDa_LIVE_URL: /hub_state'in canlı veriyi çekeceği hub adresi
SCaDa_LIVE_URL="http://127.0.0.1:8000/api/SCaDa/live_data" \
  python -m uvicorn predict_service:app --host 127.0.0.1 --port 8010
```

Doğrulama: `curl http://127.0.0.1:8010/hub_state` → 24 kompresör (health/rul).
(PowerShell'de env: `$env:SCaDa_LIVE_URL="..."; python -m uvicorn ...`)

---

## 5. SCaDaClient (Kişi 4 C#) — DB'ye yazar + tahmin çağırır

Repo **kökünden** (tek satır env + çalıştır):
```bash
SCaDaApi__BaseUrl="http://127.0.0.1:8000" \
Database__ConnectionString="Server=127.0.0.1;Port=3306;Database=pipeline_digital_twin;User ID=root;Password=SCaDa1974.;SslMode=Preferred" \
PredictionApi__BaseUrl="http://127.0.0.1:8010" \
PredictionApi__UseModelService="true" \
  dotnet run --project SCaDaClient/SCaDaClient.csproj -c Release
```
PowerShell'de:
```powershell
$env:SCaDaApi__BaseUrl="http://127.0.0.1:8000"
$env:Database__ConnectionString="Server=127.0.0.1;Port=3306;Database=pipeline_digital_twin;User ID=root;Password=SCaDa1974.;SslMode=Preferred"
$env:PredictionApi__BaseUrl="http://127.0.0.1:8010"
$env:PredictionApi__UseModelService="true"
dotnet run --project SCaDaClient/SCaDaClient.csproj -c Release
```

Beklenen log her 5 sn:
```
[DB OK] snapshot_id=N, varlık=192, narrow_telemetri=1822, ...
```

---

## 6. UI (Kişi 3 WPF)

Repo **kökünden**:
```bash
SCaDa_LIVE_URL="http://127.0.0.1:8000/api/SCaDa/live_data" \
MODEL_HUB_URL="http://127.0.0.1:8010/hub_state" \
  dotnet run --project ui/SCaDaDashboard/SCaDaDashboard.csproj -c Debug
```
Açılınca **Ayarlar → veri kaynağı "Canlı"** (üst barda "CANLI" yazmalı). Harita
düğümleri model sağlığı/RUL ile canlı güncellenir (servis kapalıysa titreşim
proxy'sine düşer, UI çökmez).

---

## 7. Doğrulama (MySQL dolu mu)

```bash
docker exec SCaDa-mysql mysql -uroot -p'SCaDa1974.' pipeline_digital_twin -e "
SELECT 'entities' t, COUNT(*) n FROM entities
UNION ALL SELECT 'telemetry_readings', COUNT(*) FROM telemetry_readings
UNION ALL SELECT 'predictions', COUNT(*) FROM predictions;
SELECT entity_id, rul_value, health_state FROM predictions
  WHERE entity_id LIKE 'CS-%' ORDER BY prediction_id DESC LIMIT 5;"
```
Beklenen: entities=192, telemetry_readings büyüyor, predictions dolu; kompresörler
`health_state=healthy`, `rul_value≈1500` (model çıktısı).

---

## 8. Kapatma

```bash
# Servisler: terminallerde Ctrl+C, veya:
taskkill //IM SCaDaClient.exe //F ; taskkill //IM SCaDaDashboard.exe //F
# uvicorn/python'ları port sahibinden kapat (PID = netstat son sütun)
# MySQL:
docker stop SCaDa-mysql
```

---

## Sorun giderme (bu makinede yaşananlar)

| Belirti | Neden / çözüm |
|---------|---------------|
| `ModuleNotFoundError: degradation` | Publisher'ı `hub/` dizininden çalıştır (sys.path shim kökü ekler). |
| SCaDaClient `InvalidOperationException ... JsonExtensionData` | Çözüldü (SegmentTelemetry çift extension-data). Güncel kodda yok. |
| `/hub_state` hep aynı değer | Kişi 2 hub kapalı → dosya fallback'i. `SCaDa_LIVE_URL`'ü canlı hub'a ver. |
| `MySqlException: Unable to connect` | MySQL container kapalı → `docker start SCaDa-mysql`. |
| UI net10 vs SCaDaClient net9 | Çözüldü, ikisi de net9. |
| `dotnet build` MSB3027 "file locked" | Çalışan `.exe`'yi kapat (`taskkill //IM ...`), tekrar derle. |
| predictions boş | `PredictionApi__UseModelService=true` ve predict_service :8010 açık olmalı. |
```
