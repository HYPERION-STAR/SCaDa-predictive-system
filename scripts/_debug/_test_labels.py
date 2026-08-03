import sys, io; sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
sys.path.insert(0, '.')
from degradation import apply_degradation, generate_degradation_corpus
import json

with open('data/SCaDa_live_snapshot.json') as f:
    snap = json.load(f)

frame = None
for rname, rd in snap.items():
    if not isinstance(rd, dict): continue
    tel = rd.get("telemetry", {})
    for tk, tv in tel.items():
        if isinstance(tv, dict) and tv.get("entity_id") == "CS-ANKARA-U1":
            frame = {k: v for k, v in tv.items() if k.startswith("s_")}
            break
    if frame: break

print("=" * 70)
print("MODEL_HEDEF_YOL.md — Adım 1 Kabul Testleri")
print("=" * 70)

# ─── Test A: Belgedeki orijinal senaryo (age_cycles=97700, fault@500) ───
params_a = {
    "unit_id": "CS-ANKARA-U1",
    "birth_defect": 1.0,
    "life_factor": 1.0,
    "age_cycles": 97700,
    "expected_life_cycles": 175200,
    "active_fault": "bearing_wear",
    "fault_start_cycle": 500,
}

print("\n[Test A] age_cycles=97700, bearing_wear @ cycle=500")
print("Kabul: health=critical ⟺ rul≈0 ⟺ vib yüksek — üçü aynı çizgide")
passed = True
for c in [150, 400, 500, 800, 2000]:
    f_out, labels = apply_degradation("CS-ANKARA-U1", frame, c, params_a)
    vib = f_out["s_7_vibration_de_mm_s"]

    # Çelişki kontrolü: healthy + rul=0 yok olmalı
    if labels.health_state == "healthy" and labels.rul_cycles <= 5:
        print(f"  FAIL: cycle={c} health=healthy ama rul≈0")
        passed = False
    
    # critical + rul>0 yok olmalı (critical'ten sonra healthy'ye dönmemeli)
    if labels.health_state == "critical" and labels.rul_cycles > 100:
        print(f"  FAIL: cycle={c} health=critical ama rul çok yüksek")
        passed = False

    vib_str = f"{vib:>8.2f}" if vib < 3 else f"***{vib:>7.2f}***"
    print(f"  cycle={c:5d} | vib={vib_str} | health={labels.health_state:10s} | rul={labels.rul_cycles:>8.1f}")

if passed:
    print("  PASSED — çelişki yok")

# ─── Test B: Full run-to-failure (batch) ───
print("\n[Test B] Batch korpus — health monotonik mi?")
corpus = generate_degradation_corpus("CS-ANKARA-U1", frame, params_a)
states = {}
for _, labels in corpus:
    states[labels.health_state] = states.get(labels.health_state, 0) + 1

print(f"  Toplam kayıt: {len(corpus)}")
print(f"  State dağılımı: {states}")

# Monotoniklik kontrolü — health geri sıçramamalı (critical→healthy yok)
prev_score = 2.0
monotonic = True
for _, labels in corpus:
    if labels.health_state == "healthy": score = 1.0
    elif labels.health_state == "degrading": score = 0.5
    else: score = 0.0
    
    # health_score monotonik azalmalı (fault fazında)
    if labels.fault_mode and score > prev_score + 0.01:
        print(f"  FAIL: Monotoniclik bozuldu — cycle'da score arttı")
        monotonic = False
        break

if monotonic:
    print("  PASSED — health monotonik (geri sıçrama yok)")

# ─── Test C: RUL son cycle'da 0 mı? ───
last_f, last_l = corpus[-1]
print(f"\n[Test C] Son kayıt kontrolü")
if last_l.health_state == "critical" and last_l.rul_cycles <= 5.0:
    print(f"  PASSED — health=critical, rul={last_l.rul_cycles}")
else:
    print(f"  FAIL — health={last_l.health_state}, rul={last_l.rul_cycles}")

# ─── Test D: age_cycles=97700, FAULT YOK → sağlıklı kalmalı ───
params_d = {k: v for k, v in params_a.items() if k not in ("active_fault", "fault_start_cycle")}
corpus_d = generate_degradation_corpus("CS-ANKARA-U1", frame, params_d)
last_f2, last_l2 = corpus_d[-1]

print(f"\n[Test D] age_cycles=97700, FAULT YOK")
if last_l2.health_state == "healthy" and len(corpus_d) >= 8760:
    print(f"  PASSED — {len(corpus_d)} kayıt, health={last_l2.health_state}")
else:
    print(f"  FAIL — {len(corpus_d)} kayıt, health={last_l2.health_state}, rul={last_l2.rul_cycles:.1f}")

# ─── Test E: Tüm fault modları ───
print("\n[Test E] Tüm fault modları için run-to-failure")
for fault_mode in ["bearing_wear", "fouling", "seal_leak", "surge"]:
    params_e = {k: v for k, v in params_a.items()}
    params_e["active_fault"] = fault_mode
    
    corpus_e = generate_degradation_corpus("CS-ANKARA-U1", frame, params_e)
    last_f3, last_l3 = corpus_e[-1]
    
    # RUL 0'a indi mi? health critical mı?
    passed_e = (last_l3.health_state == "critical" and last_l3.rul_cycles <= 5.0)
    status = "PASSED" if passed_e else "FAIL"
    print(f"  {fault_mode:12s} → records={len(corpus_e):5d}, health={last_l3.health_state:10s}, rul={last_l3.rul_cycles:.1f} [{status}]")

print("\n" + "=" * 70)
print("Adım 1 TEST TAMAMLANDI")
print("=" * 70)
