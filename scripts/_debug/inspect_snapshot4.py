"""Snapshot telemetry detayı — CS-ANKARA-U1 sensörleri."""
import json
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

with open("data/SCaDa_live_snapshot.json") as f:
    data = json.load(f)

# CS-ANKARA-U1 telemetry'sini bul
found = False
for region_name, region_data in data.items():
    if not isinstance(region_data, dict):
        continue
    tel = region_data.get("telemetry", {})
    for t_key, t_val in tel.items():
        if isinstance(t_val, dict) and "CS-ANKARA-U1" in t_val:
            comp = t_val["CS-ANKARA-U1"]
            print(f"Region: {region_name}, key: {t_key}")
            print(f"Entity ID: {comp.get('entity_id')}")
            print(f"\nAll keys ({len(comp)}):")
            for k, v in comp.items():
                if isinstance(v, dict):
                    print(f"  {k}: dict({len(v)})")
                    for ik, iv in list(v.items())[:5]:
                        print(f"    {ik}: {iv}")
                else:
                    print(f"  {k}: {v}")
            found = True
            break
    if found:
        break

if not found:
    print("CS-ANKARA-U1 bulunamadi. Ilk telemetry yapisi:")
    for region_name, region_data in list(data.items())[:1]:
        if not isinstance(region_data, dict):
            continue
        tel = region_data.get("telemetry", {})
        for t_key, t_val in list(tel.items())[:1]:
            print(f"  {t_key}: type={type(t_val).__name__}")
            if isinstance(t_val, dict):
                for k, v in list(t_val.items())[:2]:
                    print(f"    {k}: type={type(v).__name__}")
                    if isinstance(v, dict):
                        print(f"      keys: {list(v.keys())[:10]}")
