"""Tum kompresorlerin sensor degerlerini topla — SENSOR_RANGES icin."""
import json
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

with open("data/SCaDa_live_snapshot.json") as f:
    data = json.load(f)

# Sensor min/max topla
sensor_stats = {}  # name -> {min, max, count}

for region_name, region_data in data.items():
    if not isinstance(region_data, dict):
        continue
    tel = region_data.get("telemetry", {})
    for t_key, t_val in tel.items():
        if not isinstance(t_val, dict):
            continue
        # entity_type compressor olanlari al
        if t_val.get("entity_type") != "compressor":
            continue
        
        for k, v in t_val.items():
            if k.startswith("s_") and isinstance(v, (int, float)):
                if k not in sensor_stats:
                    sensor_stats[k] = {"min": v, "max": v, "count": 0}
                sensor_stats[k]["min"] = min(sensor_stats[k]["min"], v)
                sensor_stats[k]["max"] = max(sensor_stats[k]["max"], v)
                sensor_stats[k]["count"] += 1

print(f"Toplam kompresor telemetry key: {sum(1 for r in data.values() if isinstance(r, dict) for t in r.get('telemetry', {}).values() if isinstance(t, dict) and t.get('entity_type') == 'compressor')}")
print(f"\nSensor istatistikleri ({len(sensor_stats)} sensor):")
for name in sorted(sensor_stats.keys()):
    s = sensor_stats[name]
    print(f"  {name:40s} min={s['min']:>12.4f}  max={s['max']:>12.4f}  count={s['count']}")
