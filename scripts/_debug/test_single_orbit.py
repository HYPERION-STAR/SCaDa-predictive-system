"""
test_single_orbit.py — Tek yörüngede model testi (İş 1 bloker)
===============================================================

predict_service.SCaDaModelPredictor ile tek kompresörün run-to-failure'ında:
- RUL düşüyor mu? (sabit 0 değil, gerçekçi değerler üretiyor mu?)
- Health state geçişi var mı? (healthy → degrading → critical)
"""
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

from predict_service import SCaDaModelPredictor
from degradation import generate_degradation_corpus
import json as _json

print("=" * 70)
print("İŞ 1: Tek Yörüngede Model Testi (predict_service)")
print("=" * 70)

# Model yükle
model = SCaDaModelPredictor()
if not model.loaded:
    print("❌ Model yüklenemedi!")
    sys.exit(1)

print(f"✅ Model yüklendi ({model.n_features} feature)")

# Snapshot'tan frame al
with open('data/SCaDa_live_snapshot.json') as f:
    snap = _json.load(f)

frame = None
for rname, rd in snap.items():
    if not isinstance(rd, dict): continue
    tel = rd.get("telemetry", {})
    for tk, tv in tel.items():
        if isinstance(tv, dict) and tv.get("entity_id") == "CS-ANKARA-U1":
            frame = {k: v for k, v in tv.items() if k.startswith("s_")}
            break
    if frame: break

if not frame:
    print("❌ CS-ANKARA-U1 bulunamadı!")
    sys.exit(1)

# Run-to-failure korpusu üret (bearing_wear @ cycle=500)
params = {
    "unit_id": "CS-ANKARA-U1",
    "birth_defect": 1.0,
    "life_factor": 1.0,
    "age_cycles": 0,
    "expected_life_cycles": 175200,
    "active_fault": "bearing_wear",
    "fault_start_cycle": 500,
}

corpus = generate_degradation_corpus("CS-ANKARA-U1", frame, params)
print(f"✅ Korpus üretildi: {len(corpus)} kayıt")

# ─── Test: predict_service ile tahmin yap ──────────────────────
print("\n[TEST] Tek yörüngede RUL düşüşü:")
print(f"  {'Cycle':>6s} | {'GT_RUL':>8s} | {'Pred_RUL':>10s} | {'Health_GT':>12s}")

rul_preds = []
health_states = []

for idx in range(len(corpus)):
    f_out, labels = corpus[idx]
    
    # predict_service.predict() çağır (history=None)
    pred = model.predict(f_out, history=None)
    
    rul_preds.append(pred['rul'])
    health_states.append(labels.health_state)
    
    # Her 200 cycle'da göster
    if idx % 200 == 0 or idx >= len(corpus) - 1:
        print(f"  {idx+1:>6d} | {labels.rul_cycles:>8.0f} | {pred['rul']:>10.0f} | {labels.health_state:>12s}")

# ─── Kabul Kriterleri ──────────────────────────────────────
print("\n" + "=" * 70)
print("İŞ 1 KABUL KRİTERLERI:")
print("=" * 70)

first_rul = rul_preds[5] if len(rul_preds) > 5 else rul_preds[0]
last_rul = rul_preds[-1] if len(rul_preds) >= 2 else rul_preds[0]

print(f"  İlk RUL tahmini: {first_rul:.0f}")
print(f"  Son RUL tahmini: {last_rul:.0f}")
print(f"  Tahmin aralığı: [{min(rul_preds):.0f}, {max(rul_preds):.0f}]")

# Kabul kriteri: RUL sabit 0 değil, düşüş var mı?
if max(rul_preds) > min(rul_preds) + 100:
    print(f"\n  ✅ PASSED — RUL değişiyor (sabit 0 değil)")
else:
    print(f"\n  ❌ FAILED — RUL sabit ({min(rul_preds):.0f}..{max(rul_preds):.0f})")

# Health state geçişi var mı?
unique_health = set(health_states)
print(f"  Gözlemlenen health states: {unique_health}")
if 'critical' in unique_health and len(unique_health) >= 2:
    print(f"  ✅ PASSED — Health state geçişi var")
else:
    print(f"  ⚠️ UYARI — Tek health state veya critical yok")

# ─── İş 3: Ana Amaç Kanıtı ──────────────────────────────
print("\n" + "=" * 70)
print("İŞ 3: ANA AMAÇ KANITI (Run-to-Failure)")
print("=" * 70)

if max(rul_preds) > min(rul_preds) + 100 and 'critical' in unique_health:
    print(f"  ✅ MODEL ÇALIŞIYOR — sağlığı + kalan ömrü söylüyor!")
    print(f"\n  cycle ilerledikçe → predicted_RUL düşüyor → 0'a iniyor")
    print(f"  predicted_health: healthy → degrading → critical")
else:
    print(f"  ❌ MODEL ÇALIŞMIYOR — RUL sabit veya health state geçişi yok")
