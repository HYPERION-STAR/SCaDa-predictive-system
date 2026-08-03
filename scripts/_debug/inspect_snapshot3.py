"""Snapshot verisini derinlemesine incele — equipment ve telemetry."""
import json
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

with open("data/SCaDa_live_snapshot.json") as f:
    data = json.load(f)

# İlk bölgedeki equipment ve telemetry'yi incele
region = data.get("ic_anadolu", {})
equip_list = region.get("equipment", [])
print(f"Equipment list count: {len(equip_list)}")
if equip_list:
    e = equip_list[0]
    print(f"\nFirst equipment keys: {list(e.keys())}")
    for k, v in e.items():
        if isinstance(v, (dict, list)):
            print(f"  {k}: type={type(v).__name__}, len={len(v)}")
        else:
            print(f"  {k}: {v}")

# Telemetry yapısını incele
telemetry = region.get("telemetry", {})
print(f"\nTelemetry keys ({len(telemetry)}):")
for k, v in list(telemetry.items())[:5]:
    if isinstance(v, dict):
        print(f"  {k}: dict with {len(v)} items")
        # İlk item'ı göster
        for ik, iv in list(v.items())[:1]:
            print(f"    {ik}: {json.dumps(iv, indent=4)[:300]}")
    else:
        print(f"  {k}: {v}")

# Tüm bölgelerdeki equipment sayısını topla
total_equip = 0
for region_name, region_data in data.items():
    if isinstance(region_data, dict):
        eq = region_data.get("equipment", [])
        total_equip += len(eq)
        # Telemetry'deki kompresörleri de say
        tel = region_data.get("telemetry", {})
        for t_key, t_val in tel.items():
            if isinstance(t_val, dict):
                for comp_id, comp_data in t_val.items():
                    if isinstance(comp_data, dict) and "sensors" in comp_data:
                        total_equip += 1

print(f"\nTotal equipment (list + telemetry sensors): {total_equip}")

# Telemetry'deki kompresörleri detaylı göster
print("\n=== TELEMETRY COMPRESSOR DETAIL ===")
for region_name, region_data in list(data.items())[:2]:
    if not isinstance(region_data, dict):
        continue
    tel = region_data.get("telemetry", {})
    for t_key, t_val in tel.items():
        if isinstance(t_val, dict):
            for comp_id, comp_data in list(t_val.items())[:1]:
                if isinstance(comp_data, dict) and "sensors" in comp_data:
                    print(f"\n{region_name}/{t_key}/{comp_id}:")
                    sensors = comp_data.get("sensors", [])
                    print(f"  Sensors ({len(sensors)}):")
                    for s in sensors[:10]:
                        if isinstance(s, dict):
                            print(f"    {s.get('name')}: {s.get('value')}")
                        else:
                            print(f"    {s}")
