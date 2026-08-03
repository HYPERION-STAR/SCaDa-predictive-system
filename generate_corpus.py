"""
generate_corpus.py — Offline run-to-failure eğitim korpusu üretir.

Kişi 1 (AI/Model) — S6: Offline run-to-failure korpusu üret

Snapshot'ın 39 kompresörü × fault_schedule.yaml → degradation.py → korpus CSV.
Plan §7 MVP: önce 2 kompresörde doğrula, sonra 39'a genişlet.

Çıktı: data/train_SCaDa.csv — her satır bir çevrim, etiketler dahil.
"""
from __future__ import annotations

import csv
import json
import os
import sys
import io
from pathlib import Path

# Proje root'u
ROOT = Path(__file__).parent.resolve()
sys.path.insert(0, str(ROOT))

from degradation import apply_degradation, generate_degradation_corpus, SENSOR_RANGES


def load_snapshot(path: str = "data/live_data.json") -> dict:
    """Snapshot verisini yükle.

    Iki format destekler:
      1. Eski SCaDa_live_snapshot.json — region/telemetry hiyerarsisi
      2. Yeni live_data.json — flat unit_id anahtarlar (Kişi 2)
    """
    with open(path, encoding='utf-8') as f:
        raw = json.load(f)

    # Flat format kontrolü (Kişi 2 — live_data.json):
    # Anahtarlar direkt entity_id, entity_type ve s_* anahtarları doğrudan üst seviyede
    first_val = next(iter(raw.values()), None) if raw else None
    if isinstance(first_val, dict) and 'entity_type' in first_val:
        # Flat format — eski kodla uyumlu hale getir
        converted = {"live": {"telemetry": {}}}
        for unit_id, data in raw.items():
            if not isinstance(data, dict): continue
            entity_type = data.get('entity_type', '')
            telemetry = {}
            # s_* anahtarlarini telemetry'ye ekle (dogrudan, __flat__ icinde degil)
            for k, v in data.items():
                if k.startswith('s_') and isinstance(v, (int, float)):
                    telemetry[k] = v
            telemetry['entity_type'] = entity_type
            telemetry['entity_id'] = unit_id
            converted["live"]["telemetry"][unit_id] = telemetry
        return converted

    return raw


def load_fault_schedule(path: str = "scenarios/fault_schedule.yaml") -> dict:
    """Fault schedule'ı yükle (basit YAML parser — key-value format)."""
    # Basit YAML desteği: sadece fault_schedule.yaml formatını destekler
    try:
        import yaml
        with open(path) as f:
            data = yaml.safe_load(f)
        return data.get("units", {})
    except ImportError:
        # PyYAML yoksa, JSON fallback kullan
        json_path = path.replace(".yaml", ".json")
        if os.path.exists(json_path):
            with open(json_path) as f:
                return json.load(f).get("units", {})
        return {}


def extract_compressor_frames(snapshot: dict) -> list[tuple[str, dict]]:
    """
    Snapshot'tan kompresör telemetry'lerini çıkar.

    Returns:
        [(unit_id, healthy_frame), ...] listesi
    """
    compressors = []

    for region_name, region_data in snapshot.items():
        if not isinstance(region_data, dict):
            continue
        tel = region_data.get("telemetry", {})
        for t_key, t_val in tel.items():
            if not isinstance(t_val, dict):
                continue
            if t_val.get("entity_type") != "compressor":
                continue

            unit_id = t_val.get("entity_id", f"{region_name}_{t_key}")
            # s_1...s_21 sensörlerini al
            frame = {}
            for key, val in t_val.items():
                if key.startswith("s_") and isinstance(val, (int, float)):
                    frame[key] = val

            if len(frame) >= 10:  # en az 10 sensör var
                compressors.append((unit_id, frame))

    return compressors


def generate_corpus(
    snapshot: dict,
    fault_schedule: dict,
    max_cycles_per_unit: int = 8760,
    output_path: str = "data/train_SCaDa.csv",
) -> str:
    """
    Tüm kompresörlerden run-to-failure korpusu üret.

    Args:
        snapshot: SCaDa_live_snapshot.json içeriği
        fault_schedule: fault_schedule.yaml içeriği (units dict)
        max_cycles_per_unit: maksimum çevrim sayısı
        output_path: çıktı CSV yolu

    Returns:
        output_path — yazılan dosya yolu
    """
    compressors = extract_compressor_frames(snapshot)
    print(f"Toplam kompresör bulundu: {len(compressors)}")

    # Çıktı CSV başlıkları
    sensor_cols = sorted(SENSOR_RANGES.keys())
    header = (
        ["unit_id", "cycle", "health_state", "fault_mode", "rul_cycles", "health_score"]
        + sensor_cols
    )

    total_records = 0
    state_dist = {}

    with open(output_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(header)

        for idx, (unit_id, healthy_frame) in enumerate(compressors):
            # MVP: ilk 2 kompresörde doğrula → sonra tüm 39'a genişlet
            # Şimdilik tüm ünitelerle çalış:
            # if idx >= 2:
            #     continue

            # Fault schedule'dan parametreleri al
            fs_entry = fault_schedule.get(unit_id, {})
            faults = fs_entry.get("faults", [])

            # Unit params
            params = {
                "unit_id": unit_id,
                "birth_defect": 1.0,
                "life_factor": 1.0,
                "age_cycles": 0,
                "expected_life_cycles": 175200,
            }

            # Fault schedule uygula — ramp_cycles da geçir (degradation.py kullanır)
            if faults:
                fault = faults[0]
                params["active_fault"] = fault.get("fault_mode")
                params["fault_start_cycle"] = fault.get("start_cycle", 99999)
                # Per-unit ramp_cycles — degradation.py'ye geçir
                custom_ramp = fault.get("severity_ramp_cycles")
                if custom_ramp is not None:
                    params["custom_ramp_cycles"] = int(custom_ramp)

            # Korpus üret (run-to-failure)
            corpus = generate_degradation_corpus(
                unit_id=unit_id,
                healthy_frame=healthy_frame,
                unit_params=params,
                total_cycles=max_cycles_per_unit,
            )

            for cycle_idx, (frame, labels) in enumerate(corpus, 1):
                row = [
                    unit_id,
                    cycle_idx,
                    labels.health_state,
                    labels.fault_mode,
                    round(labels.rul_cycles, 1),
                    round(labels.health_score, 4),
                ]
                # Sensör değerlerini sıralı ekle
                for col in sensor_cols:
                    row.append(round(frame.get(col, 0.0), 6))

                writer.writerow(row)
                total_records += 1
                state_dist[labels.health_state] = state_dist.get(labels.health_state, 0) + 1

    print(f"\nKorpus kaydedildi: {output_path}")
    print(f"Toplam kayıt: {total_records}")
    print(f"State dağılımı: {state_dist}")

    return output_path


if __name__ == "__main__":
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

    print("=" * 70)
    print("Offline Run-to-Failure Korpus Üretimi (S6)")
    print("=" * 70)

    # Snapshot yükle — öncelik live_data.json (Kişi 2), fallback SCaDa_live_snapshot.json
    snapshot_path = ROOT / "data" / "live_data.json"
    if not snapshot_path.exists():
        snapshot_path = ROOT / "data" / "SCaDa_live_snapshot.json"
    print(f"\n[1/3] Snapshot yükleniyor: {snapshot_path}")
    snapshot = load_snapshot(str(snapshot_path))

    # Fault schedule yükle
    fs_path = ROOT / "scenarios" / "fault_schedule.yaml"
    print(f"[2/3] Fault schedule yükleniyor: {fs_path}")
    fault_schedule = load_fault_schedule(str(fs_path))
    print(f"  Fault schedule üniteleri: {len(fault_schedule)}")

    # Korpus üret
    output_path = str(ROOT / "data" / "train_SCaDa.csv")
    print(f"[3/3] Korpus üretiliyor → {output_path}")
    result = generate_corpus(snapshot, fault_schedule, max_cycles_per_unit=8760, output_path=output_path)

    # CSV özet
    import pandas as pd
    df = pd.read_csv(result)
    print(f"\nCSV özeti:")
    print(f"  Satır sayısı: {len(df)}")
    print(f"  Sütun sayısı: {len(df.columns)}")
    print(f"  Benzersiz üniteler: {df['unit_id'].nunique()}")
    print(f"  State dağılımı:\n{df['health_state'].value_counts()}")
    print(f"  Fault mode dağılımı:\n{df['fault_mode'].value_counts()}")
    print(f"  RUL range: [{df['rul_cycles'].min():.0f}, {df['rul_cycles'].max():.0f}]")

    print("\n" + "=" * 70)
    print("Korpus üretimi tamamlandi!")
    print("=" * 70)
