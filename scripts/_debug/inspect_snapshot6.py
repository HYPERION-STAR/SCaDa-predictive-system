"""Equipment detayı ve tüm kompresör sensörlerini topla."""
import json
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

with open("data/SCaDa_live_snapshot.json") as f:
    data = json.load(f)

# Tüm bölgelerdeki equipment'leri topla
all_compressors = []
for region_name, region_data in data.items():
    if not isinstance(region_data, dict):
        continue
    eq_list = region_data.get("equipment", [])
    for e in eq_list:
        if e.get("unit_type") == "compressor":
            all_compressors.append((region_name, e))

print(f"Toplam kompresör equipment: {len(all_compressors)}")

# İlk 3 kompresörün tüm alanlarını göster
for region, e in all_compressors[:3]:
    print(f"\n=== {region} / {e.get('unit_id')} ===")
    for k, v in e.items():
        if isinstance(v, (dict, list)):
            print(f"  {k}: type={type(v).__name__}, len={len(v)}")
            if isinstance(v, dict):
                for ik, iv in list(v.items())[:5]:
                    print(f"    {ik}: {iv}")
            elif isinstance(v, list) and len(v) > 0:
                item = v[0]
                if isinstance(item, dict):
                    print(f"    first: {json.dumps(item, indent=6)[:500]}")
                else:
                    print(f"    first: {item}")
        else:
            print(f"  {k}: {v}")

# Tüm kompresörlerin sensör isimlerini topla (benzersiz)
all_sensor_names = set()
for region, e in all_compressors:
    for section_name, section_data in e.items():
        if isinstance(section_data, list):
            for item in section_data:
                if isinstance(item, dict) and "name" in item:
                    all_sensor_names.add(item["name"])

print(f"\nBenzersiz sensör isimleri ({len(all_sensor_names)}):")
for name in sorted(all_sensor_names):
    print(f"  {name}")
