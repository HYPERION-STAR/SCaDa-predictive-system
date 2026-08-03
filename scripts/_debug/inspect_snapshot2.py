"""Snapshot verisini derinlemesine incele."""
import json
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

with open("data/SCaDa_live_snapshot.json") as f:
    data = json.load(f)

# Bölgeleri gez
for region_name, region_data in list(data.items())[:2]:
    print(f"\n=== {region_name} ===")
    if isinstance(region_data, dict):
        for k, v in region_data.items():
            print(f"  {k}: type={type(v).__name__}, len={len(v) if isinstance(v, (list,dict)) else 'N/A'}")
            if isinstance(v, list) and len(v) > 0:
                item = v[0]
                if isinstance(item, dict):
                    print(f"    first item keys: {list(item.keys())[:15]}")
                    # equipment veya sensors ara
                    for ik, iv in item.items():
                        if isinstance(iv, dict) and 'sensors' in iv:
                            print(f"    {ik} has sensors!")
                            for s in iv['sensors'][:3]:
                                print(f"      {s}")
