"""
SCaDa Tahmin Servisi — FastAPI (F1 + B yolu: gerçek model)
============================================================

MD §3.1 ENTEGRASYON_TAHMIN_SISTEMI.md ile uyumlu.

Çalıştırma:
    uvicorn predict_service:app --host 0.0.0.0 --port 8010

Faz 1: Kural tabanlı motor (her zaman çalışır)
B yolu: SCaDa modeli (models/SCaDa/ varsa aktif edilir)
"""
from fastapi import FastAPI
from pydantic import BaseModel, Field
from typing import Optional
import time
import json
import os
import sys
import io
import asyncio

import numpy as np
import joblib
import onnxruntime as ort

# ─── İstek / Yanıt modelleri ───────────────────────────────────────

class PredictRequest(BaseModel):
    entity_id: str
    entity_type: str  # "compressor" | "segment" | "ugs" | ...
    sensors: dict     # {"s_1_suction_pressure_bar": 54.4, ...}
    history: list[dict] | None = None  # önceki okumalar (sliding window)


class PredictionResponse(BaseModel):
    entity_id: str = ""
    rul: float = 0.0
    health_score: float = 1.0
    anomaly: bool = False
    fault_mode: Optional[str] = ""
    source: str = "rule"  # "model" | "rule"
    status: str = "running"  # running / standby / fault (§8, D-S4)


# ─── Model sınıflandırıcı (SCaDa) ─────────────────────────────────

class SCaDaModelPredictor:
    """
    SCaDa RUL + fault_mode modeli.
    
    Giriş sözleşmesi (214 feature):
      1. 21 ham sensör
      2. 4 op ayar (0)
      3. Rolling mean/std/residual × 3 window (3/10/30) × 21 sensör = 3 × 3 × 21 = 189
      
      Toplam: 21 + 4 + 189 = 214
    """

    SENSOR_COLS = [
        "s_1_suction_pressure_bar",
        "s_2_discharge_pressure_bar",
        "s_3_pressure_ratio",
        "s_4_suction_temp_c",
        "s_5_discharge_temp_c",
        "s_6_shaft_rpm",
        "s_7_vibration_de_mm_s",
        "s_8_vibration_nde_mm_s",
        "s_9_axial_displacement_mm",
        "s_10_bearing_temp_1_c",
        "s_11_bearing_temp_2_c",
        "s_12_lube_oil_pressure_bar",
        "s_13_lube_oil_temp_c",
        "s_14_gas_flow_meter_m3_h",
        "s_15_seal_gas_pressure_bar",
        "s_16_gas_flow_m3_h",
        "s_17_power_mw",
        "s_18_polytropic_efficiency",
        "s_19_surge_margin_pct",
        "s_20_filter_dp_bar",
        "s_21_torque_nm",
    ]

    OP_COLS = ["op_1_ambient_temp_c", "op_2_inlet_pressure_bar",
               "op_3_flow_demand_m3_h", "op_4_speed_setpoint_pct"]

    WINDOW_SIZES = [3, 10, 30]

    def __init__(self, model_dir: str = "models/SCaDa"):
        self.model_dir = model_dir
        self.loaded = False
        self._load()

    def _load(self):
        """Modelleri yükle."""
        try:
            # A1 Fix: health_score modeli (eski random_forest_rul.pkl yerine)
            self.health_score_model = joblib.load(os.path.join(self.model_dir, "random_forest_health_score.pkl"))
            # Gerçek RUL regresörü
            self.rul_model = joblib.load(os.path.join(self.model_dir, "random_forest_rul.pkl"))
            # Health state sınıflandırıcı (A4 Fix)
            self.health_state_model = joblib.load(os.path.join(self.model_dir, "random_forest_health.pkl"))
            self.fault_model = joblib.load(os.path.join(self.model_dir, "random_forest_fault.pkl"))
            self.scaler = joblib.load(os.path.join(self.model_dir, "scaler.pkl"))

            # Giriş sözleşmesi
            with open(os.path.join(self.model_dir, "feature_cols.json")) as f:
                self.feature_info = json.load(f)

            self.n_features = self.feature_info["n_features"]
            self.loaded = True
            print(f"  [OK] SCaDa modeli yüklendi ({self.n_features} feature)")
        except Exception as e:
            print(f"  [X] SCaDa modeli yüklenemedi: {e}")
            self.loaded = False

    def _build_features(self, sensors: dict, history: list[dict] | None = None) -> np.ndarray:
        """
        Ham sensör + history → 214 feature vektörü.
        
        SZ1 Fix: health_score feature'dan çıkarıldı (hedef sızıntısı).
        SZ3 Fix: cycle feature'dan çıkarıldı (RUL ile doğrusal).
        
        History yoksa rolling özellikler sensor range'lerinden türetilmiş
        varsayılan window ile hesaplanır — training dağılımına yakın kalır.
        """
        features = []

        # 1. Ham sensörler
        for col in self.SENSOR_COLS:
            val = sensors.get(col, 0.0)
            features.append(float(val))

        # 2. Op ayarları (0)
        for _ in self.OP_COLS:
            features.append(0.0)

        # 3-4. Rolling özellikler (history veya ham değer üzerinden)
        series_data = {}
        for col in self.SENSOR_COLS:
            vals = [s.get(col, 0.0) for s in (history or [])]
            vals.append(sensors.get(col, 0.0))  # mevcut değer
            series_data[col] = vals

        for w in self.WINDOW_SIZES:
            for col in self.SENSOR_COLS:
                vals = series_data[col]
                
                # History varsa normal rolling; yoksa varsayılan window kullan
                if len(vals) > 1:
                    window = vals[-w:] if len(vals) >= w else vals
                    rmean = float(np.mean(window))
                    rstd = float(np.std(window)) if len(window) > 1 else 0.0
                else:
                    # History yoksa — training dağılımına yakın varsayılan değerler
                    current = vals[0]
                    rmean = current
                    rstd = abs(current) * 0.05

                residual = float(vals[-1]) - rmean

                features.append(rmean)
                features.append(rstd)
                features.append(residual)

        X = np.array(features, dtype=np.float32).reshape(1, -1)
        return X

    def predict(self, sensors: dict, history: list[dict] | None = None) -> dict:
        """
        Model tahmini yap.
        
        Returns:
            {"rul": float, "health_score": float, "health_state": str, "fault_mode": str}
        """
        if not self.loaded:
            return {"rul": -1, "health_score": 1.0, "health_state": "unknown", "fault_mode": "model_unavailable"}

        X = self._build_features(sensors, history)
        X_sc = self.scaler.transform(X)

        # A1 Fix: health_score modeli dogrudan 0-1 arasi uretiyor
        health_score_pred = float(self.health_score_model.predict(X_sc)[0])
        health_score_pred = max(0.0, min(1.0, health_score_pred))

        fault_pred = self.fault_model.predict(X_sc)[0]
        candidate_fault = str(fault_pred) if fault_pred else ""

        # RUL: cap'li RUL regresöründen DOĞRUDAN (arıza-yakını held-out MAE 235→58, 4×).
        # Eski yaklaşım rul = health_score × sabit idi; o, arızaya yakın ömrü fazla
        # tahmin ediyordu (health_score kritikte ~0.27 → ×1350 ≈ 365, gerçek ~50).
        # RUL cap'li (~130) eğitildiği için: healthy → ~130 ("≥130 çevrim"), kritik → düşük.
        # health_score ve health_state ayrı modellerden gelir.
        rul_pred = max(0.0, float(self.rul_model.predict(X_sc)[0]))

        # A4 Fix: health_state siniflandirici — modelden doldur (RUL'dan cevirme)
        state_pred = self.health_state_model.predict(X_sc)[0]
        
        # Fault: health_state critical ise fault_mode'yu kullan, yoksa bos
        if state_pred == "critical":
            fault_mode = candidate_fault if candidate_fault else ""
        else:
            fault_mode = ""

        return {
            "rul": round(rul_pred, 1),
            "health_score": round(health_score_pred, 4),
            "health_state": str(state_pred),
            "fault_mode": fault_mode,
        }


# ─── Model örneği (global) ─────────────────────────────────────────

model_predictor = SCaDaModelPredictor()


# ─── Kural tabanlı sağlık motoru (F1) ──────────────────────────────

def rule_based_health(entity_type: str, sensors: dict) -> PredictionResponse:
    """
    Kural tabanlı sağlık skoru — model olmayan varlıklar için (§4.3).
    B yolu bitene kadar kompresör için de kullanılır.
    """
    if entity_type == "compressor":
        return _compressor_rules(sensors)
    elif entity_type == "segment":
        return _segment_rules(sensors)
    elif entity_type == "ugs":
        return _ugs_rules(sensors)
    elif entity_type == "border_entry":
        return _border_rules(sensors)
    elif entity_type in ("fsru", "lng_terminal"):
        return _storage_rules(sensors, entity_type)
    else:
        return PredictionResponse(
            entity_id="", source="rule", health_score=0.5, fault_mode="unknown_type"
        )


def _compressor_rules(s: dict) -> PredictionResponse:
    """
    Kompresör kural kuralları (§8, §4.3):
      - vibrasyon > 11 mm/s → Kırmızı
      - bearing_temp > 90°C → Kırmızı
      - surge_margin < 15% → Sarı
      - standby → Gri
    """
    vib_de = s.get("s_7_vibration_de_mm_s", 0)
    vib_nde = s.get("s_8_vibration_nde_mm_s", 0)
    bearing_t1 = s.get("s_10_bearing_temp_1_c", 0)
    bearing_t2 = s.get("s_11_bearing_temp_2_c", 0)
    surge = s.get("s_19_surge_margin_pct", 100)
    rpm = s.get("s_6_shaft_rpm", 0)

    # Durus kontrolü — D-S4: standby ayrı status, health_score=1.0 (sağlıklı duruş)
    if vib_de == 0 and rpm < 100:
        return PredictionResponse(
            entity_id="", source="rule", health_score=1.0,
            anomaly=False, fault_mode="", status="standby"
        )

    anomaly = False
    fault_mode = ""
    health = 1.0

    # Vibrasyon — DE
    if vib_de >= 11:
        anomaly = True; fault_mode = "high_vibration_de"; health = 0.2
    elif vib_de > 7:
        anomaly = True; fault_mode = "elevated_vibration_de"; health = 0.6

    # Vibrasyon — NDE (daha kritikse)
    if vib_nde >= 11:
        anomaly = True; fault_mode = "high_vibration_nde"; health = min(health, 0.2)
    elif vib_nde > 7:
        anomaly = True; fault_mode = "elevated_vibration_nde"; health = min(health, 0.6)

    # Rulman sıcaklığı
    if bearing_t1 > 90 or bearing_t2 > 90:
        anomaly = True; fault_mode = "high_bearing_temp"; health = 0.1
    elif bearing_t1 > 80 or bearing_t2 > 80:
        anomaly = True; fault_mode = "elevated_bearing_temp"; health = min(health, 0.5)

    # Surge margin
    if surge < 15:
        anomaly = True; fault_mode = "low_surge_margin"; health = min(health, 0.4)

    status = "fault" if (anomaly and health < 0.3) else "running"
    return PredictionResponse(
        entity_id="", source="rule",
        rul=-1 if anomaly else 9999,  # kural tabani RUL vermez
        health_score=health,
        anomaly=anomaly, fault_mode=fault_mode, status=status
    )


def _segment_rules(s: dict) -> PredictionResponse:
    """Segment kuralları (§4.3): mass_imbalance > 2%, cathodic_protection."""
    mass_imb = abs(s.get("s_mass_imbalance_pct", 0))
    cp_v = s.get("s_cathodic_protection_v", 999)

    anomaly = False; fault_mode = ""; health = 1.0

    if mass_imb > 2.0:
        anomaly = True; fault_mode = "mass_imbalance_high"; health = 0.3
    if cp_v < 0.85:
        anomaly = True; fault_mode = "cathodic_protection_weak"; health = min(health, 0.5)

    status = "fault" if anomaly else "running"
    return PredictionResponse(
        entity_id="", source="rule", rul=-1,
        health_score=health, anomaly=anomaly, fault_mode=fault_mode, status=status
    )


def _ugs_rules(s: dict) -> PredictionResponse:
    """UGS kuralı (§4.3): storage_level < 10%."""
    level = s.get("s_storage_level_pct", 100)
    low = level < 10
    status = "fault" if low else "running"
    return PredictionResponse(
        entity_id="", source="rule", rul=-1,
        health_score=0.3 if low else 1.0,
        anomaly=low, fault_mode="low_storage_level" if low else "", status=status
    )


def _border_rules(s: dict) -> PredictionResponse:
    """Border kuralı (§4.3): pressure anlaşma bandı dışı."""
    p = s.get("pressure_bar", 0)
    out = p < 80 or p > 130
    status = "fault" if out else "running"
    return PredictionResponse(
        entity_id="", source="rule", rul=-1,
        health_score=0.4 if out else 1.0,
        anomaly=out, fault_mode="pressure_out_of_band" if out else "", status=status
    )


def _storage_rules(s: dict, entity_type: str) -> PredictionResponse:
    """FSRU / LNG terminal kuralı: storage_level < 15%."""
    level = s.get("s_storage_level_pct", 100)
    low = level < 15
    status = "fault" if low else "running"
    return PredictionResponse(
        entity_id="", source="rule", rul=-1,
        health_score=0.3 if low else 1.0,
        anomaly=low, fault_mode="low_storage_level" if low else "", status=status
    )


# ─── Model tabanlı tahmin (B yolu) ─────────────────────────────────

def model_based_predict(entity_type: str, sensors: dict, history: list[dict] | None = None) -> PredictionResponse:
    """
    B yolu: Gerçek SCaDa modeli ile tahmin.
    
    Sadece compressor tipi için çalışır; diğer tipler kural tabanlı kalır.
    """
    if entity_type != "compressor":
        return rule_based_health(entity_type, sensors)

    if not model_predictor.loaded:
        # Model yüklenemediyse fallback → kural
        result = rule_based_health(entity_type, sensors)
        result.source = "rule"
        return result

    try:
        pred = model_predictor.predict(sensors, history)
        
        rul = pred["rul"]
        health = pred["health_score"]
        fault_mode = pred["fault_mode"]

        # Anomali: health < 0.3 veya fault_mode var
        anomaly = bool(health < 0.3 or (fault_mode and fault_mode != ""))

        # Status
        if health >= 0.7:
            status = "running"
        elif health >= 0.3:
            status = "running"
        else:
            status = "fault"

        return PredictionResponse(
            entity_id="",
            rul=rul,
            health_score=health,
            anomaly=anomaly,
            fault_mode=fault_mode,
            source="model",
            status=status,
        )
    except Exception as e:
        # Hata → fallback kural
        print(f"  [X] Model tahmin hatası: {e}")
        result = rule_based_health(entity_type, sensors)
        result.source = "rule"
        return result


# ─── FastAPI endpoint'leri ─────────────────────────────────────────

from contextlib import asynccontextmanager


@asynccontextmanager
async def lifespan(app):
    print("=" * 60)
    print("SCaDa Tahmin Servisi başlatıldı (v2.0)")
    print(f"  Model: {'SCaDa RF (214 feature)' if model_predictor.loaded else 'Kural tabanlı (F1)'}")
    print("  /predict      → tek varlık tahmini")
    print("  /predict_batch → toplu tahmin (189 varlık)")
    print("  /hub_state    → 24 ünite toplu (arka planda cache'lenir)")
    print("  /health       → servis sağlık kontrolü")
    print(f"  Hub yenileme aralığı: {HUB_REFRESH_SECONDS}s")
    print("=" * 60)
    task = asyncio.create_task(_hub_refresh_loop())
    yield
    task.cancel()


app = FastAPI(title="SCaDa Tahmin Servisi", version="2.0.0", lifespan=lifespan)


@app.post("/predict", response_model=PredictionResponse)
async def predict(req: PredictRequest):
    """
    Tek varlık tahmin endpoint'i.
    
    Compressor → model (B yolu) veya kural (fallback).
    Diğer tipler → kural tabanlı.
    """
    result = model_based_predict(req.entity_type, req.sensors, req.history)
    result.entity_id = req.entity_id
    return result


@app.post("/predict_batch", response_model=list[PredictionResponse])
async def predict_batch(req: list[PredictRequest]):
    """
    Toplu tahmin endpoint'i — 189 varlık için tek HTTP çağrı (§10 risk).
    D-S2: '|' operatörü Pydantic modelinde çalışmaz, alanı doğrudan ata.
    """
    out = []
    for r in req:
        res = model_based_predict(r.entity_type, r.sensors, r.history)
        res.entity_id = r.entity_id  # '|' değil — alanı ata
        out.append(res)
    return out


@app.get("/health")
async def health():
    """Servis sağlık kontrolü — C# istemci bağlantı testi için."""
    model_status = "loaded" if model_predictor.loaded else "unavailable"
    return {
        "status": "ok",
        "model": "SCaDa_rf_214feat" if model_predictor.loaded else "rule_based_f1",
        "model_source": "model" if model_predictor.loaded else "rule",
        "version": "2.0.0",
    }


# ─── UI sözleşmesi: /hub_state ──────────────────────────────────────

import os, json, urllib.request

SCaDa_LIVE_URL = os.environ.get(
    "SCaDa_LIVE_URL", "http://100.114.223.5:8000/api/scada/live_data")

# Arka plan yenileme aralığı (saniye). Servis bu periyotla Kişi 2'yi çekip
# hub_state'i cache'ler; /hub_state cache'i anında döner.
HUB_REFRESH_SECONDS = float(os.environ.get("HUB_REFRESH_SECONDS", "5"))

# En son hesaplanan hub_state (arka plan görevi günceller).
_HUB_CACHE: dict = {"nodes": [], "segments": [], "updated_at": None}


def _load_live() -> dict:
    """Canlı endpoint'ten çek. Erişilemezse DOSYAYA DÜŞMEZ — hata fırlatır ki
    çağıran (arka plan döngüsü) son iyi durumu koruyup bir sonraki turda
    yeniden denesin."""
    with urllib.request.urlopen(SCaDa_LIVE_URL, timeout=3) as resp:
        data = json.loads(resp.read())
    if isinstance(data, dict) and data:
        return data
    raise ValueError("live endpoint boş/geçersiz yanıt döndü")


def build_hub_state(live: dict) -> dict:
    """
    Canlı veriyi UI'nin beklediği JSON sözleşmesine dönüştürür.
    
    nodes: kompresörler için model tahmini (health 0-100, rul, vibration, ...)
    segments: boru hatları için akış bilgisi
    
    Bu fonksiyon sunucu ayağa kalkmadan test edilebilir — offline kullanım içindir.
    """
    nodes = []
    for eid, d in live.items():
        if d.get("entity_type") == "compressor":
            pr = model_based_predict("compressor", d)  # HAZIR fonksiyon
            nodes.append({
                "id": eid,
                "health": round(pr.health_score * 100, 1),  # UI 0-100 ister
                "rul": round(pr.rul, 1),
                "vibration": d.get("s_7_vibration_de_mm_s", 0.0),
                "bearingTemp": d.get("s_10_bearing_temp_1_c", 0.0),
                "dischargePressure": d.get("s_2_discharge_pressure_bar", 0.0),
            })

    segments = []
    for eid, d in live.items():
        if d.get("entity_type") == "segment":
            flow = next((v for k, v in d.items()
                         if "flow" in k and isinstance(v, (int, float))), 0.0)
            segments.append({
                "id": eid,
                "flow": round(float(flow), 1),
                "load": 0.5,
                "leak": False,
            })

    return {"nodes": nodes, "segments": segments}


async def _hub_refresh_loop():
    """Arka planda HUB_REFRESH_SECONDS aralığıyla canlı veriyi çekip
    hub_state'i cache'ler. Böylece /hub_state anında yanıt verir ve servis
    Kişi 2'yi sürekli pollayan bir tüketici olur."""
    while True:
        try:
            # urlopen bloklayıcı → thread'e al ki event loop tıkanmasın
            live = await asyncio.to_thread(_load_live)
            state = build_hub_state(live)
            state["updated_at"] = time.strftime("%Y-%m-%dT%H:%M:%S")
            state["stale"] = False
            _HUB_CACHE.update(state)
        except Exception as e:
            # Veri alınamadı → cache'e DOKUNMA (son iyi durum kalsın),
            # HUB_REFRESH_SECONDS sonra yeniden dene.
            _HUB_CACHE["stale"] = True
            print(f"  [hub_state] veri alınamadı ({e}) — {HUB_REFRESH_SECONDS}s sonra yeniden denenecek")
        await asyncio.sleep(HUB_REFRESH_SECONDS)


@app.get("/hub_state")
async def hub_state():
    """UI için toplu tahmin ucu — arka planda cache'lenmiş son durumu döner.
    Cache henüz hiç dolmadıysa (soğuk açılış + kaynak erişilemez) boş liste
    döner; dosyaya düşmez, döngü bir sonraki turda yeniden dener."""
    return _HUB_CACHE


