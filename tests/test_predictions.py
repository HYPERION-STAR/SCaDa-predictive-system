"""
Hafta 4: Test Senaryolari
==========================
SCaDa Kestirimci Bakim - Tahmin Testleri
"""
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

import os
import json

# src/ dizinini path'e ekle
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))
from model_predictor import Predictor

print("=" * 60)
print("SCaDa Kestirimci Bakim - Test Suite")
print("=" * 60)

# Test sensor degerleri (saglikli motor referans degerleri)
HEALTHY = {
    'sensor_1': 250.0, 'sensor_2': 1000.0, 'sensor_3': 1.0,
    'sensor_4': 1800.0, 'sensor_5': 21.0, 'sensor_6': 100.0,
    'sensor_7': 500.0, 'sensor_8': 2000.0, 'sensor_9': 150.0,
    'sensor_10': 20.0, 'sensor_11': 3000.0, 'sensor_12': 50.0,
    'sensor_13': 1000.0, 'sensor_14': 250.0, 'sensor_15': 2000.0,
    'sensor_16': 30.0, 'sensor_17': 150.0, 'sensor_18': 1800.0,
    'sensor_19': 250.0, 'sensor_20': 100.0, 'sensor_21': 0.05,
}

passed = 0
failed = 0
tests = []


def test(name, condition, details=""):
    global passed, failed
    status = "PASS" if condition else "FAIL"
    if condition:
        passed += 1
    else:
        failed += 1
    tests.append((name, status, details))
    print(f"  [{status}] {name}" + (f" - {details}" if details else ""))


# =========================================================================
# Test 1: Model Yukleme
# =========================================================================
print("\n[Test 1] Model Yukleme")
try:
    predictor = Predictor()
    test("Model dosyalari yuklendi", True)
except Exception as e:
    test(f"Model dosyalari yuklendi", False, str(e))
    print("\nModel yuklenemedi, testler durduruldu.")
    sys.exit(1)

# =========================================================================
# Test 2: Normal (Saglikli) Motor
# =========================================================================
print("\n[Test 2] Saglikli Motor Tahmini")
result = predictor.predict(HEALTHY)
test("RUL pozitif", result['rul'] > 0, f"RUL={result['rul']}")
test("Health score 0-100 araliginda", 0 <= result['health_score'] <= 100,
     f"Score={result['health_score']}")
test("Saglikli motor yuksek skor", result['health_score'] > 50,
     f"Score={result['health_score']}")
test("Anomaly=False (saglikli)", result['anomaly'] == False,
     f"anomaly={result['anomaly']}")

# =========================================================================
# Test 3: Agir Bozulmus Motor
# =========================================================================
print("\n[Test 3] Agir Bozulmus Motor")
degraded = HEALTHY.copy()
degraded['sensor_21'] = 0.20    # 4x artti
degraded['sensor_3'] = 0.3      # 3x dustu
degraded['sensor_6'] = 200.0    # 2x artti
degraded['sensor_12'] = 120.0   # 2.4x artti
degraded['sensor_14'] = 100.0   # 2x dustu
degraded['sensor_15'] = 3500.0  # 1.75x artti

result = predictor.predict(degraded)
test("Bozulmus motor dusuk RUL", result['rul'] < 100, f"RUL={result['rul']}")
test("Bozulmus motor health score < saglikli",
     result['health_score'] < predictor.predict(HEALTHY)['health_score'],
     f"Degraded={result['health_score']}, Healthy={predictor.predict(HEALTHY)['health_score']}")

# =========================================================================
# Test 4: Anomali Tespiti
# =========================================================================
print("\n[Test 4] Anomali Tespiti")
spike = HEALTHY.copy()
spike['sensor_5'] = 999.0  # Cok yuksek deger (sensör arizasi)

result = predictor.predict(spike)
test("Anomali tespit edildi mi", result['anomaly'] == True or result['is_degraded'] == True,
     f"anomaly={result['anomaly']}, degraded={result['is_degraded']}")

# =========================================================================
# Test 5: Cikti Format
# =========================================================================
print("\n[Test 5] Cikti Format Dogrulama")
result = predictor.predict(HEALTHY)
test("rul float", isinstance(result['rul'], (int, float)))
test("health_score float", isinstance(result['health_score'], (int, float)))
test("anomaly bool", isinstance(result['anomaly'], bool))
test("is_degraded bool", isinstance(result['is_degraded'], bool))

# =========================================================================
# Test 6: Toplu Tahmin
# =========================================================================
print("\n[Test 6] Toplu Tahmin")
data = [HEALTHY, degraded, spike]
results = predictor.predict_batch(data)
test("3 tahmin dondu", len(results) == 3, f"len={len(results)}")
test("Her tahmin dict", all(isinstance(r, dict) for r in results))

# =========================================================================
# Test 7: Model Bilgisi
# =========================================================================
print("\n[Test 7] Model Bilgisi")
info = predictor.model_info()
test("sensor_count = 21", info['sensor_count'] == 21, f"count={info['sensor_count']}")
test("n_features > 0", info['n_features'] > 0, f"features={info['n_features']}")
test("max_rul > 0", info['max_rul'] > 0, f"max_rul={info['max_rul']}")

# =========================================================================
# Test 8: Feature Onemliligi
# =========================================================================
print("\n[Test 8] Feature Onemliligi")
top_feats = predictor.get_feature_importance(5)
test("5 feature dondu", len(top_feats) == 5, f"len={len(top_feats)}")
test("Feature isimleri str", all(isinstance(f[0], str) for f in top_feats))
test("Importance degerleri float", all(isinstance(f[1], float) for f in top_feats))
test("Importance sirali (azalan)",
     all(top_feats[i][1] >= top_feats[i+1][1] for i in range(len(top_feats)-1)))

# =========================================================================
# Test 9: JSON Serilestirme
# =========================================================================
print("\n[Test 9] JSON Serilestirme")
try:
    json_str = json.dumps(result)
    parsed = json.loads(json_str)
    test("JSON round-trip basarili", parsed['rul'] == result['rul'])
except Exception as e:
    test("JSON round-trip basarili", False, str(e))

# =========================================================================
# Test 10: Kose Durumlar
# =========================================================================
print("\n[Test 10] Kose Durumlar")
# Eksik sensörler
partial = {'sensor_1': 250.0, 'sensor_21': 0.05}
try:
    result = predictor.predict(partial)
    test("Eksik sensörlerle tahmin yapabiliyor", True)
except Exception as e:
    test(f"Eksik sensörlerle tahmin yapabiliyor", False, str(e))

# =========================================================================
# Sonuclari Ozetle
# =========================================================================
print("\n" + "=" * 60)
print("TEST SONUCLARI")
print("=" * 60)
for name, status, details in tests:
    icon = "✓" if status == "PASS" else "✗"
    print(f"  {icon} {name}")

print(f"\nToplam: {passed + failed} test")
print(f"  Basarili: {passed}")
print(f"  Basarisiz: {failed}")

if failed == 0:
    print("\nTum testler basarili!")
else:
    print(f"\n{failed} test basarisiz!")

print("=" * 60)
