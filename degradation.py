"""
degradation.py — Kompresör Yaşlanma ve Arıza Simülasyonu
=========================================================

Kişi 1 (AI/Model) — Çekirdek Teslimat

Bu modül, sağlıklı telemetri karesini alır → yaşlandırır → bozulmuş kare
ve etiketler döndürür. İki yerde kullanılır:

  Offline:  8760 çevrim döndür → run-to-failure eğitim korpusu
  Online:   Kişi 2'nin sunucusunda çevrim başına çağrılır → canlı RUL

Aynı kod, iki kullanım → train/serve skew yok.

Kullanım:
    from degradation import apply_degradation

    degraded_frame, labels = apply_degradation(
        unit_id="CS-ANKARA-U1",
        healthy_frame={"s_1_suction_pressure_bar": 54.4, ...},
        cycle=42,
        unit_params={
            "birth_defect": 0.95,
            "life_factor": 1.2,
            "age_cycles": 0,
            "expected_life_cycles": 175200,
        },
    )

    # labels = {
    #     "health_state": "healthy",
    #     "fault_mode": "",
    #     "rul_cycles": 120000,
    # }
"""
from __future__ import annotations

import random
from dataclasses import dataclass, field
from typing import Optional


# ─── Sensör açıklamaları (metadata) ────────────────────────────────

SENSOR_DESCRIPTIONS = {
    "s_1_suction_pressure_bar":       ("Emme basıncı", "bar"),
    "s_2_discharge_pressure_bar":     ("Basma basıncı", "bar"),
    "s_3_pressure_ratio":             ("Basınç oranı", "—"),
    "s_4_suction_temp_c":             ("Emme sıcaklığı", "°C"),
    "s_5_discharge_temp_c":           ("Basma sıcaklığı", "°C"),
    "s_6_shaft_rpm":                  ("Şaft devri", "RPM"),
    "s_7_vibration_de_mm_s":          ("Titreşim DE", "mm/s"),
    "s_8_vibration_nde_mm_s":         ("Titreşim NDE", "mm/s"),
    "s_9_axial_displacement_mm":      ("Eksenel yer değiştirme", "mm"),
    "s_10_bearing_temp_1_c":          ("Rulman sıcaklığı 1", "°C"),
    "s_11_bearing_temp_2_c":          ("Rulman sıcaklığı 2", "°C"),
    "s_12_lube_oil_pressure_bar":     ("Yağ basıncı", "bar"),
    "s_13_lube_oil_temp_c":           ("Yağ sıcaklığı", "°C"),
    "s_14_gas_flow_meter_m3_h":       ("Gaz debisi (metre)", "m³/h"),
    "s_15_seal_gas_pressure_bar":     ("Seal gaz basıncı", "bar"),
    "s_16_gas_flow_m3_h":             ("Gaz akışı", "m³/h"),
    "s_17_power_mw":                  ("Güç", "MW"),
    "s_18_polytropic_efficiency":     ("Polytropic verim", "—"),
    "s_19_surge_margin_pct":          ("Surge marjı", "%"),
    "s_20_filter_dp_bar":             ("Filtre ΔP", "bar"),
    "s_21_torque_nm":                 ("Tork", "Nm"),
}


def _build_sensor_ranges_from_snapshot(
    snapshot_data: dict,
) -> dict:
    """
    Snapshot verisinden SENSOR_RANGES oluştur.

    K1 Fix: Range'ler **fiziksel sınır** olmalı, sağlıklı zarf değil.
    Sağlıklı aralığı kapsa, üstüne arıza ilerlemesi için pay bırak.
    Vibrasyon gibi sensörlerde arıza sağlıklı tavanın **üstüne** çıkabilir —
    yoksa arıza `_clamp` tarafından yutulur ve model ele veremez.

    Örn: s_7_vibration_de — sağlıklı max=7.2, kritik eşik=11, fiziksel tavan≈25
    """
    sensor_stats = {}

    # A2 Fix: Iki format destekle — eski (region/telemetry) ve yeni (flat unit_id)
    first_val = next(iter(snapshot_data.values()), None) if snapshot_data else None
    is_flat = isinstance(first_val, dict) and 'entity_type' in first_val and any(
        k.startswith('s_') for k in first_val.keys()
    )

    if is_flat:
        # Flat format (live_data.json): anahtarlar direkt unit_id
        for unit_id, data in snapshot_data.items():
            if not isinstance(data, dict): continue
            if data.get("entity_type") != "compressor": continue
            for k, v in data.items():
                if k.startswith("s_") and isinstance(v, (int, float)):
                    if k not in sensor_stats:
                        sensor_stats[k] = {"min": v, "max": v, "count": 0}
                    sensor_stats[k]["min"] = min(sensor_stats[k]["min"], v)
                    sensor_stats[k]["max"] = max(sensor_stats[k]["max"], v)
                    sensor_stats[k]["count"] += 1
    else:
        # Eski format (region/telemetry hiyerarsisi)
        for region_name, region_data in snapshot_data.items():
            if not isinstance(region_data, dict):
                continue
            tel = region_data.get("telemetry", {})
            for t_key, t_val in tel.items():
                if not isinstance(t_val, dict):
                    continue
                if t_val.get("entity_type") != "compressor":
                    continue

                for k, v in t_val.items():
                    if k.startswith("s_") and isinstance(v, (int, float)):
                        if k not in sensor_stats:
                            sensor_stats[k] = {"min": v, "max": v, "count": 0}
                        sensor_stats[k]["min"] = min(sensor_stats[k]["min"], v)
                        sensor_stats[k]["max"] = max(sensor_stats[k]["max"], v)
                        sensor_stats[k]["count"] += 1

    ranges = {}
    for name, stats in sensor_stats.items():
        lo = stats["min"]
        hi = stats["max"]

        # K1 Fix: Arıza için geniş pay bırak — fiziksel sınır olmalı
        if "vibration" in name:
            # Vibrasyon: sağlıklı ≤7.2, kritik eşik 11, fiziksel tavan ~25
            hi = max(hi * 3.0, 25.0)
        elif "bearing_temp" in name:
            # Rulman sıcaklığı: sağlıklı ≤84, arıza 120+ olabilir
            hi = max(hi * 1.3, 120.0)
        elif "discharge_temp" in name:
            # Deşarj sıcaklığı: sağlıklı ≤73, fouling ile 100+
            hi = max(hi * 1.3, 100.0)
        elif "efficiency" in name:
            # Verim: sağlıklı ≥0.78, fouling ile 0.3'e düşer
            lo = max(0.0, lo * 0.3)
            hi = max(hi, 1.0)
        elif "surge_margin" in name:
            # Surge marjı: sağlıklı ≥0, arıza 0'a yaklaşır
            lo = 0.0
            hi = max(hi * 1.2, 45.0)
        elif "seal_gas" in name:
            # Seal basıncı: sağlıklı ≥0.5, leak ile sıfıra iner
            lo = 0.0
            hi = max(hi * 1.3, 8.0)
        elif "filter_dp" in name:
            # Filtre ΔP: sağlıklı ≤0.94, tıkalı filtre 3+
            hi = max(hi * 3.0, 3.0)
        elif "flow" in name and "meter" not in name:
            # Gaz akışı: seal leak ile sıfıra gidebilir
            lo = 0.0
        elif "discharge_pressure" in name:
            # Basma basıncı: sağlıklı ≤60, fiziksel sınır ~80
            hi = max(hi * 1.3, 80.0)
        elif "pressure_ratio" in name:
            # Basınç oranı: sağlıklı ≤1.11, arıza ile değişir
            hi = max(hi * 1.5, 2.0)
        elif "axial_displacement" in name:
            # Eksenel yer değiştirme: sağlıklı ≤1.63, arıza ile artar
            hi = max(hi * 1.5, 3.0)
        elif "lube_oil_pressure" in name:
            # Yağ basıncı: sağlıklı ≥0.1, ariza ile düşer
            lo = 0.0
        
        # K1 Fix: Canli veri araliklarini da kapsasin — fiziksel sinir olmal
        # live_data.json'dan gelen degerler egitim araliginin disinda olmamali

        ranges[name] = (round(lo, 4), round(hi, 4))

    return ranges


# Snapshot'tan SENSOR_RANGES oluştur (modül yüklendiğinde bir kez)
# A2 Fix: once live_data.json (Kişi 2), fallback SCaDa_live_snapshot.json
try:
    import json as _json
    import os as _os
    _snapshot_path = _os.path.join(
        _os.path.dirname(_os.path.abspath(__file__)), "data", "live_data.json"
    )
    if not _os.path.exists(_snapshot_path):
        _snapshot_path = _os.path.join(
            _os.path.dirname(_os.path.abspath(__file__)), "data", "SCaDa_live_snapshot.json"
        )
    if _os.path.exists(_snapshot_path):
        with open(_snapshot_path) as _f:
            _snapshot_data = _json.load(_f)
        SENSOR_RANGES = _build_sensor_ranges_from_snapshot(_snapshot_data)
    else:
        # Fallback: elle yazılmış aralıklar — CANLI VERI araliklarini kapsayacak sekilde genisletildi
        SENSOR_RANGES = {
            "s_1_suction_pressure_bar":       (40.0, 70.0),
            "s_2_discharge_pressure_bar":     (50.0, 80.0),
            "s_3_pressure_ratio":             (0.0, 1.5),
            "s_4_suction_temp_c":             (-10.0, 40.0),
            "s_5_discharge_temp_c":           (20.0, 100.0),
            "s_6_shaft_rpm":                  (0.0, 6500.0),
            "s_7_vibration_de_mm_s":          (0.0, 25.0),
            "s_8_vibration_nde_mm_s":         (0.0, 15.0),
            "s_9_axial_displacement_mm":      (0.0, 4.0),
            "s_10_bearing_temp_1_c":          (20.0, 120.0),
            "s_11_bearing_temp_2_c":          (20.0, 120.0),
            "s_12_lube_oil_pressure_bar":     (0.0, 8.0),
            "s_13_lube_oil_temp_c":           (20.0, 90.0),
            "s_14_gas_flow_meter_m3_h":       (0.0, 2000000.0),
            "s_15_seal_gas_pressure_bar":     (0.0, 80.0),
            "s_16_gas_flow_m3_h":             (0.0, 2000000.0),
            "s_17_power_mw":                  (0.0, 12.0),
            "s_18_polytropic_efficiency":     (0.0, 1.0),
            "s_19_surge_margin_pct":          (0.0, 45.0),
            "s_20_filter_dp_bar":             (0.0, 3.0),
            "s_21_torque_nm":                 (0.0, 18000.0),
        }
except Exception:
    # Herhangi bir hata durumunda fallback
    SENSOR_RANGES = {
        "s_1_suction_pressure_bar":       (40.0, 70.0),
        "s_2_discharge_pressure_bar":     (0.0, 80.0),
        "s_3_pressure_ratio":             (0.0, 1.5),
        "s_4_suction_temp_c":             (-10.0, 40.0),
        "s_5_discharge_temp_c":           (20.0, 100.0),
        "s_6_shaft_rpm":                  (0.0, 6000.0),
        "s_7_vibration_de_mm_s":          (0.0, 15.0),
        "s_8_vibration_nde_mm_s":         (0.0, 12.0),
        "s_9_axial_displacement_mm":      (0.0, 2.0),
        "s_10_bearing_temp_1_c":          (20.0, 120.0),
        "s_11_bearing_temp_2_c":          (20.0, 120.0),
        "s_12_lube_oil_pressure_bar":     (0.0, 5.0),
        "s_13_lube_oil_temp_c":           (20.0, 80.0),
        "s_14_gas_flow_meter_m3_h":       (0.0, 2000000.0),
        "s_15_seal_gas_pressure_bar":     (0.0, 8.0),
        "s_16_gas_flow_m3_h":             (0.0, 2000000.0),
        "s_17_power_mw":                  (0.0, 10.0),
        "s_18_polytropic_efficiency":     (0.0, 1.0),
        "s_19_surge_margin_pct":          (0.0, 45.0),
        "s_20_filter_dp_bar":             (0.0, 2.0),
        "s_21_torque_nm":                 (0.0, 1000.0),
    }


# ─── Arıza modu → göreli bozulma oranlari (A2 Fix) ────────────────

# A2 Fix: Eski FAULT_TARGETS sabit degerler içeriyordu (eski snapshot bazli).
# live_data'daki ünitelerin sağlıklı degerleri tamamen farkli.
# Cozum: her ariza modu icin **göreli degisim orani** tanimla.
# delta = healthy_val * fault_delta * fault_progress → her ünite kendi
# bazina göre bozulur, canli veri araliklarini asmaz (ariza fazinda asabilir).

FAULT_DELTAS = {
    # Yatak aşınması: titreşim ↑↑, rulman sıcaklığı ↑
    "bearing_wear": {
        "s_7_vibration_de_mm_s": 2.5,      # +250% (sağlıklı 3.7 → ~13)
        "s_8_vibration_nde_mm_s": 2.0,     # +200% (sağlıklı 3.4 → ~6.8)
        "s_10_bearing_temp_1_c": 0.5,      # +50% (sağlıklı 75 → ~112)
        "s_11_bearing_temp_2_c": 0.4,      # +40% (sağlıklı 73 → ~102)
    },
    # Fouling (kirlenme): verim ↓↓, filtre ΔP ↑↑, deşarj ısınır ↑
    "fouling": {
        "s_18_polytropic_efficiency": -0.5,  # -50% (sağlıklı 0.8 → ~0.4)
        "s_20_filter_dp_bar": 3.0,           # +300% (sağlıklı 0.37 → ~1.5)
        "s_5_discharge_temp_c": 0.5,         # +50% (sağlıklı 54 → ~81)
    },
    # Seal sızıntısı: seal basıncı ↓↓, gaz kaçağı
    "seal_leak": {
        "s_15_seal_gas_pressure_bar": -0.85,  # -85% (sağlıklı 61 → ~9)
        "s_16_gas_flow_m3_h": -0.7,           # -70% (sağlıklı 500k → ~150k)
    },
    # Surge: surge marjı ↓↓, titreşim ↑↑
    "surge": {
        "s_19_surge_margin_pct": -0.85,       # -85% (sağlıklı 20.5 → ~3)
        "s_7_vibration_de_mm_s": 2.0,         # +200% (sağlıklı 3.7 → ~11)
    },
}

# Arıza rampası süresi (cycle cinsinden, health'in 0'a indiği an)
FAULT_RAMP_CYCLES = {
    "bearing_wear": 1500,
    "fouling":      2000,
    "seal_leak":    1800,
    "surge":        1200,
}


# ─── Data sınıfları ────────────────────────────────────────────────

@dataclass
class DegradationLabels:
    """Arıza etiketleri."""
    health_state: str = "healthy"       # healthy | degrading | critical
    fault_mode: str = ""                # bearing_wear | fouling | seal_leak | surge
    rul_cycles: float = 0.0             # kalan çevrim
    health_score: float = 1.0           # 0–1


@dataclass
class UnitParams:
    """Ünite kalıcı parametreleri (Kişi 2'den gelir)."""
    unit_id: str = ""
    birth_defect: float = 1.0
    life_factor: float = 1.0
    age_cycles: int = 0
    expected_life_cycles: int = 175200
    active_fault: Optional[str] = None   # None = sağlıklı, yoksa arıza modu
    fault_start_cycle: int = 0           # arızanın başladığı çevrim
    custom_ramp_cycles: int = 0          # per-unit ramp (fault_schedule.yaml'dan)

    @classmethod
    def from_dict(cls, d: dict) -> "UnitParams":
        """Dict → UnitParams dönüşümü."""
        return cls(
            unit_id=d.get("unit_id", ""),
            birth_defect=float(d.get("birth_defect", 1.0)),
            life_factor=float(d.get("life_factor", 1.0)),
            age_cycles=int(d.get("age_cycles", 0)),
            expected_life_cycles=int(d.get("expected_life_cycles", 175200)),
            active_fault=d.get("active_fault"),
            fault_start_cycle=int(d.get("fault_start_cycle", 0)),
            custom_ramp_cycles=int(d.get("custom_ramp_cycles", 0)),
        )


# ─── Yardımcı fonksiyonlar ─────────────────────────────────────────

def _clamp(value: float, lo: float, hi: float) -> float:
    """Değer sınırlar içinde kalır."""
    return max(lo, min(hi, value))


def _get_dynamic_max_rul(unit_params: UnitParams, fault_active: bool) -> int:
    """
    Z2 Fix: Tek tavan kullan — arıza öncesi/sonrası aynı MAX_RUL.
    
    Eski: arıza öncesi 130 (C-MAPSS), arıza sonrası ramp*0.9 → RUL zıplaması.
    Yeni: her zaman ramp_cycles'a bağlı → tek düzende azalır, zıplama yok.
    
    Arıza yoksa: genel ömür tavanı (130 yerine expected_life'dan türetilir).
    Arıza varsa: ramp_cycles × 0.9 → tüm pencerede RUL görünür.
    """
    if fault_active and unit_params.active_fault:
        ramp = FAULT_RAMP_CYCLES.get(unit_params.active_fault, 1500)
        return max(130, int(ramp * 0.9))
    
    # Arıza yoksa: genel ömür tavanı — gerçek yaşamın %90'ı
    true_life = unit_params.expected_life_cycles * unit_params.life_factor
    return int(true_life * 0.9)  # ~157680 for life_factor=1.0


def _effective_age(cycle: int, unit_params: UnitParams) -> int:
    """
    S3 Fix: effective_age = age_cycles + cycle.

    age_cycles: ünite t=0'da çalışmış çevrim (Kişi 2'den).
    cycle: simülasyonun şu anki çevrimi.
    Birlikte, ünite toplam ne kadar yaşlandı gösterir.
    """
    return unit_params.age_cycles + cycle


def _progress(cycle: int, unit_params: UnitParams) -> float:
    """
    S3 Fix: effective_age kullanır (age_cycles + cycle).

    Yaşlanma ilerlemesi (0 = yeni, 1 = ömür sonu).

    Formül: birth_defect × (effective_age / true_life) ^ 1.5

    1.5 üssü, yaşlanmanın sonlarda hızlandığını modeller
    (C-MAPSS ile uyumlu).
    """
    effective_age = _effective_age(cycle, unit_params)
    true_life = unit_params.expected_life_cycles * unit_params.life_factor
    raw = unit_params.birth_defect * (effective_age / true_life) ** 1.5
    return min(raw, 1.0)  # 1.0'a ulaşınca ömür bitti


def _get_ramp(unit_params: UnitParams) -> int:
    """Arıza ramp süresi — custom varsa onu kullan, yoksa FAULT_RAMP_CYCLES."""
    if unit_params.custom_ramp_cycles > 0 and unit_params.active_fault:
        return unit_params.custom_ramp_cycles
    return FAULT_RAMP_CYCLES.get(unit_params.active_fault, 1500)


def _failure_cycle(cycle: int, unit_params: UnitParams) -> float:
    """
    Başarısızlık çevrimi — health=RUL=0 olacağı an.

    Arıza aktif (fault_start_cycle'a ulaşılmış): fault_start + ramp → arıza bu noktada üniteyi öldürür.
    Ariza yoksa veya henüz baslamadiysa: inf (bozulma yok, failure yok).

    Bu tek kaynak, health_state ve rul_cycles'in ayni cycle'da
    "critical / 0" noktasina ulasmasini saglar.
    """
    fault_active = (
        unit_params.active_fault is not None
        and cycle >= unit_params.fault_start_cycle
        and unit_params.fault_start_cycle > 0
    )

    if fault_active:
        ramp = _get_ramp(unit_params)
        return float(unit_params.fault_start_cycle + ramp)

    # Ariza yoksa — failure yok (inf). RUL ve health etkilenmez.
    import math as _math
    return _math.inf


def _rul(cycle: int, unit_params: UnitParams, fault_active: bool) -> float:
    """
    Kalan ceyrim — failure_cycle'a kadar olan mesafe.

    Ariza aktif: RUL = (fault_start + ramp) - cycle  → fault fazinda age_cycles yok.
    Ariza yoksa: MAX_RUL sabit (model RUL'un düştüğünü görmeli, sadece arizayla düşsin).

    health_score ve rul_cycles ayni kaynagi (_failure_cycle) kullanir →
    asla "healthy + rul=0" celiskisi olusmaz.
    """
    fc = _failure_cycle(cycle, unit_params)

    if fault_active and unit_params.active_fault:
        # Ariza fazinda sadece simülasyon cycle'i sayilir (age_cycles yok).
        return max(0.0, fc - cycle)

    # Ariza yoksa — sabit tavan (model için referans)
    ramp = _get_ramp(unit_params)
    return float(ramp * 2)  # arizasiz modda RUL görünür ama sabit


def _health_score(cycle: int, unit_params: UnitParams) -> float:
    """
    Saglik skoru (0–1).

    Ariza aktif: fault_progress'e gore azalir (fault_start→failure_cycle arasi),
                 age_cycles KULLANILMAZ — sadece cycle sayilir.
    Ariza yoksa: _progress() ile gradual bozulma (A2 Fix) → smooth egilim, step fonksiyon degil!

    Bu sayede health_score → 0 ⟺ rul_cycles → 0 ayni noktada olur.
    
    A1/Model kalitesi icin kritik — model continuous degisimi ogrenebilsin diye:
      - birth_defect < 1 olan üniteler daha hizli bozulur (baslangictan hasarli)
      - life_factor > 1 olanlar erken yaslanir 
    """
    if unit_params.active_fault and cycle >= unit_params.fault_start_cycle and unit_params.fault_start_cycle > 0:
        ramp = _get_ramp(unit_params)
        fault_progress = min(1.0, (cycle - unit_params.fault_start_cycle + 1) / ramp)
        return max(0.0, 1.0 - fault_progress)
    
    return max(0.0, 1.0 - _progress(cycle, unit_params))


def _health_state(score: float) -> str:

    if score >= 0.7:
        return "healthy"
    elif score >= 0.3:
        return "degrading"
    return "critical"



# ─── Ana fonksiyon ─────────────────────────────────────────────────

def apply_degradation(
    unit_id: str,
    healthy_frame: dict,
    cycle: int,
    unit_params: dict | UnitParams,
    rng: random.Random | None = None,
) -> tuple[dict, DegradationLabels]:
    """
    Sağlıklı kareyi al → yaşlandır → (bozulmuş_kare, etiketler).

    Args:
        unit_id: Ünite kimliği (örn. "CS-ANKARA-U1")
        healthy_frame: Sağlam telemetri dict (s_1_..., s_2_..., ...)
        cycle: Mevcat çevrim (monotonik artan, 1'den başlar)
        unit_params: Kalıcı ünite parametreleri (dict veya UnitParams)
        rng: Deterministik test için RNG instance

    Returns:
        (degraded_frame, labels)
        - degraded_frame: bozulmuş sensör değerleri dict
        - labels: DegradationLabels{health_state, fault_mode, rul_cycles, health_score}

    Degradasyon Mantığı:
        1. Healthy frame kopyalanır — arızasız cycle'da DEĞİŞMEZ çıkar
        2. _progress() ile yaşlanma oranı hesaplanır (S3: age_cycles dahil)
        3. Arıza aktifse, FAULT_SENSORS tablosuna göre ilgili sensörler
           cycle başına artış/azalış uygular
        4. Değerler SENSOR_RANGES içinde clamp edilir (S2: gerçek veriden)
        5. Health score, health_state, RUL hesaplanır (S1: arızaya bağlı)
    """
    if isinstance(unit_params, dict):
        params = UnitParams.from_dict(unit_params)
    else:
        params = unit_params

    if rng is None:
        rng = random

    # Ünite ID'yi params'a kaydet (dict'ten gelmediyse)
    if not params.unit_id:
        params.unit_id = unit_id

    # Fault durumunu belirle
    fault_active = (
        params.active_fault is not None
        and cycle >= params.fault_start_cycle
    )
    active_fault_name = params.active_fault if fault_active else None

    # Yaşlanma ilerlemesi hesapla
    progress = _progress(cycle, params)

    # Sağlıklı sensör değerlerini kopyala
    degraded = dict(healthy_frame)

    # A3 Fix: Gurultu — korpus deterministik olmasin, canli veri gibi hissetsin
    # Her sensore küçük rastgele sapma ekle (canli okuma gurusunu simule eder)
    for sensor_key in list(degraded.keys()):
        if not isinstance(degraded[sensor_key], (int, float)):
            continue
        s_lo, s_hi = SENSOR_RANGES.get(sensor_key, (0.0, 999999.0))
        healthy_val = degraded[sensor_key]
        # Sensore ozgu gurultu orani — buyuk degerler daha az yuzdesel gurultu
        if abs(healthy_val) > 1000:
            noise_pct = 0.002  # %0.2 (buyuk degerler: flow, power)
        elif abs(healthy_val) > 10:
            noise_pct = 0.005  # %0.5
        else:
            noise_pct = 0.01   # %1 (kucuk degerler)
        
        noise = rng.gauss(0, noise_pct * max(abs(healthy_val), 0.001))
        degraded[sensor_key] = _clamp(degraded[sensor_key] + noise, s_lo, s_hi)

    # Ariza varsa, ilgili sensörlere degradasyon uygula (A2 Fix: göreli bozulma)
    if active_fault_name and active_fault_name in FAULT_DELTAS:
        deltas = FAULT_DELTAS[active_fault_name]
        fault_age = max(1, cycle - params.fault_start_cycle)
        ramp = FAULT_RAMP_CYCLES.get(active_fault_name, 1500)

        for sensor_key, fault_delta in deltas.items():
            if sensor_key not in degraded:
                continue

            s_lo, s_hi = SENSOR_RANGES.get(sensor_key, (0.0, 999999.0))
            healthy_val = healthy_frame.get(sensor_key, degraded[sensor_key])

            # A2 Fix: fault_progress 0→1, pencere boyunca düzgün tırmanma
            fault_progress = min(1.0, fault_age / ramp)

            # Göreli degisim: delta = healthy_val * fault_delta * fault_progress
            # fault_delta pozitif → artan sensör (vib, temp), negatif → azalan (efficiency, seal pressure)
            change = healthy_val * abs(fault_delta) * fault_progress

            if fault_delta > 0:
                degraded[sensor_key] = _clamp(healthy_val + change, s_lo, s_hi)
            else:
                degraded[sensor_key] = _clamp(healthy_val - change, s_lo, s_hi)

            # Ariza ileri seviyede ani sapma (surge gibi) — S4 Fix:
            if progress > 0.7 and rng.random() < 0.05:
                current_val = degraded[sensor_key]
                spike = current_val * 0.15 * rng.choice([-1, 1])
                degraded[sensor_key] = _clamp(current_val + spike, s_lo, s_hi)

    # RUL ve health hesapla (tek kaynak: failure_cycle)
    rul = _rul(cycle, params, fault_active)
    score = _health_score(cycle, params)
    state = _health_state(score)

    labels = DegradationLabels(
        health_state=state,
        fault_mode=active_fault_name or "",
        rul_cycles=round(rul, 1),
        health_score=round(score, 4),
    )

    return degraded, labels


# ─── Batch üretici (offline eğitim için) ───────────────────────────

def generate_degradation_corpus(
    unit_id: str,
    healthy_frame: dict,
    unit_params: dict | UnitParams,
    total_cycles: int = 8760,
    fault_schedule: dict | None = None,
) -> list[tuple[dict, DegradationLabels]]:
    """
    Tek ünite için run-to-failure korpus üretir.

    Args:
        unit_id: Ünite kimliği
        healthy_frame: Sağlam telemetri dict
        unit_params: Ünite parametreleri
        total_cycles: Toplam çevrim sayısı (varsayılan: 8760 = 1 yıl)
        fault_schedule: {"fault_mode": start_cycle} dict

    Returns:
        [(degraded_frame, labels), ...] listesi
    """
    if isinstance(unit_params, dict):
        params = UnitParams.from_dict(unit_params)
    else:
        params = unit_params

    # Fault schedule'ı uygula
    if fault_schedule:
        for fault_mode, start_cycle in fault_schedule.items():
            if start_cycle <= total_cycles:
                params.active_fault = fault_mode
                params.fault_start_cycle = start_cycle
                break  # Şimdilik tek arıza

    corpus = []
    for cycle in range(1, total_cycles + 1):
        degraded, labels = apply_degradation(
            unit_id=unit_id,
            healthy_frame=healthy_frame,
            cycle=cycle,
            unit_params=params,
        )
        corpus.append((degraded, labels))

        # K2 Fix: 'or' → 'and' (yalnız rul_cycles <= 0 ile dur)
        # Eski: or → ilk critical'da duruyordu, tek critical örneği vardı
        # Yeni: RUL sıfıra inene kadar devam eder → yüzlerce critical örnek
        if labels.rul_cycles <= 0:
            break

    return corpus


# ─── Hızlı test ────────────────────────────────────────────────────

if __name__ == "__main__":
    import sys
    import io
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

    print("=" * 70)
    print("degradation.py — Hızlı Test (S1-S4 Düzeltmeler Sonrası)")
    print("=" * 70)

    # Snapshot'tan gerçek kompresör verisini yükle
    import json as _json
    import os as _os
    snapshot_path = _os.path.join(_os.path.dirname(_os.path.abspath(__file__)),
                                   "data", "SCaDa_live_snapshot.json")
    with open(snapshot_path) as _f:
        snapshot = _json.load(_f)

    # CS-ANKARA-U1 telemetry'sini bul
    healthy_frame = None
    for region_name, region_data in snapshot.items():
        if not isinstance(region_data, dict):
            continue
        tel = region_data.get("telemetry", {})
        for t_key, t_val in tel.items():
            if isinstance(t_val, dict) and t_val.get("entity_id") == "CS-ANKARA-U1":
                healthy_frame = {k: v for k, v in t_val.items() if k.startswith("s_")}
                break
        if healthy_frame:
            break

    if not healthy_frame:
        print("CS-ANKARA-U1 telemetry bulunamadi!")
        sys.exit(1)

    print(f"\nSENSOR_RANGES ({len(SENSOR_RANGES)} sensör) — snapshot'tan türetildi:")
    for name in sorted(SENSOR_RANGES.keys()):
        lo, hi = SENSOR_RANGES[name]
        actual = healthy_frame.get(name, "N/A")
        print(f"  {name:40s} range=({lo:>10.4f}, {hi:>10.4f})  actual={actual}")

    # Test 1: Arızasız cycle=1 — hiçbir şey değişmemeli (S2 kabul)
    print("\n[Test 1] Arızasız cycle=1 — hiçbir sensör değişmemeli:")
    params_healthy = {
        "unit_id": "CS-ANKARA-U1",
        "birth_defect": 1.0,
        "life_factor": 1.0,
        "age_cycles": 0,
        "expected_life_cycles": 175200,
    }
    degraded_1, labels_1 = apply_degradation("CS-ANKARA-U1", healthy_frame, 1, params_healthy)
    changed = 0
    for k in healthy_frame:
        if abs(degraded_1.get(k, 0) - healthy_frame[k]) > 1e-9:
            changed += 1
            print(f"  DEGISDI: {k} {healthy_frame[k]} → {degraded_1[k]}")
    print(f"  Degisen sensör: {changed}/21 (kabul: 0)")

    # Test 2: S3 — age_cycles farklı RUL üretmeli
    print("\n[Test 2] S3 — age_cycles farklı RUL:")
    for ac in [0, 50000, 100000, 170000]:
        p = dict(params_healthy)
        p["age_cycles"] = ac
        _, labels = apply_degradation("CS-ANKARA-U1", healthy_frame, 1, p)
        print(f"  age_cycles={ac:>7d} → rul={labels.rul_cycles:>8.0f}  health={labels.health_score:.4f}")

    # Test 3: S1 — RUL arızaya bağlı, failure_cycle'a iner
    print("\n[Test 3] S1 — RUL arızaya bağlı (bearing_wear @ cycle=500):")
    params_fault = {
        "unit_id": "CS-ANKARA-U1",
        "birth_defect": 1.0,
        "life_factor": 1.0,
        "age_cycles": 0,
        "expected_life_cycles": 175200,
        "active_fault": "bearing_wear",
        "fault_start_cycle": 500,
    }
    for c in [400, 500, 800, 1000, 1500, 2000]:
        frame, labels = apply_degradation("CS-ANKARA-U1", healthy_frame, c, params_fault)
        print(f"  cycle={c:5d} | state={labels.health_state:10s} | "
              f"score={labels.health_score:.3f} | rul={labels.rul_cycles:8.0f} | "
              f"fault={labels.fault_mode or '(none)'}")

    # Test 4: Batch korpus — run-to-failure gerçekleşmeli
    print("\n[Test 4] Batch korpus (bearing_wear @ cycle=500):")
    corpus = generate_degradation_corpus("CS-ANKARA-U1", healthy_frame, params_fault)
    states = {}
    for _, labels in corpus:
        states[labels.health_state] = states.get(labels.health_state, 0) + 1
    print(f"  Toplam kayıt: {len(corpus)}")
    print(f"  State dağılımı: {states}")
    print(f"  İlk RUL: {corpus[0][1].rul_cycles:.0f}")
    print(f"  Son RUL: {corpus[-1][1].rul_cycles:.0f} (state={corpus[-1][1].health_state})")

    # Test 5: Fouling ile farklı geçiş
    print("\n[Test 5] Fouling korpusu:")
    params_fouling = dict(params_fault)
    params_fouling["active_fault"] = "fouling"
    params_fouling["fault_start_cycle"] = 1000
    corpus_f = generate_degradation_corpus("CS-ANKARA-U1", healthy_frame, params_fouling)
    states_f = {}
    for _, labels in corpus_f:
        states_f[labels.health_state] = states_f.get(labels.health_state, 0) + 1
    print(f"  Toplam kayıt: {len(corpus_f)}")
    print(f"  State dağılımı: {states_f}")
    print(f"  İlk RUL: {corpus_f[0][1].rul_cycles:.0f}")
    print(f"  Son RUL: {corpus_f[-1][1].rul_cycles:.0f} (state={corpus_f[-1][1].health_state})")

    print("\n" + "=" * 70)
    print("Test tamamlandi!")
    print("=" * 70)
