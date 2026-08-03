"""Snapshot telemetry detayı."""
import json
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

with open("data/SCaDa_live_snapshot.json") as f:
    data = json.load(f)

# Tüm telemetry key'lerini topla
for region_name, region_data in list(data.items())[:3]:
    if not isinstance(region_data, dict):
        continue
    tel = region_data.get("telemetry", {})
    print(f"\n=== {region_name} telemetry keys ({len(tel)}) ===")
    for t_key in sorted(tel.keys())[:10]:
        print(f"  {t_key}")

# İlk kompresör telemetry'sini detaylı göster
for region_name, region_data in data.items():
    if not isinstance(region_data, dict):
        continue
    tel = region_data.get("telemetry", {})
    for t_key, t_val in tel.items():
        if isinstance(t_val, dict) and "cs_ankara" in t_key.lower():
            comp = list(t_val.values())[0]
            print(f"\n=== {t_key} detail ===")
            print(json.dumps(comp, indent=2)[:2000])
