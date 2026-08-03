"""Snapshot verisini incele — SENSOR_RANGES için."""
import json
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

with open("data/SCaDa_live_snapshot.json") as f:
    data = json.load(f)

print("Top-level keys:", list(data.keys()))

# equipment var mi?
eq = data.get("equipment", [])
print(f"equipment count: {len(eq)}")

# nodes var mi?
nodes = data.get("nodes", [])
print(f"nodes count: {len(nodes)}")

if nodes:
    n = nodes[0]
    print(f"\nFirst node keys: {list(n.keys())[:15]}")
    print(f"First node type: {n.get('type')}")
    print(f"First node id: {n.get('id')}")

# equipment içinde kompresör ara
compressors = []
for e in eq:
    t = e.get("type", "").lower()
    if "compressor" in t or "cs" in t:
        compressors.append(e)

print(f"\nCompressors found: {len(compressors)}")

if compressors:
    c = compressors[0]
    print(f"\nFirst compressor:")
    print(f"  id: {c.get('id')}")
    print(f"  type: {c.get('type')}")
    print(f"  birth_defect: {c.get('birth_defect', 'N/A')}")
    print(f"  life_factor: {c.get('life_factor', 'N/A')}")
    print(f"  age_cycles: {c.get('age_cycles', 'N/A')}")
    print(f"  expected_life_cycles: {c.get('expected_life_cycles', 'N/A')}")

    sensors = c.get("sensors", [])
    print(f"\nSensors ({len(sensors)}):")
    for s in sensors:
        print(f"  {s.get('name')}: {s.get('value')}")

# nodes içinde de sensor var mı kontrol et
for node in nodes[:3]:
    node_sensors = node.get("sensors", [])
    if node_sensors:
        print(f"\nNode {node.get('id')} sensors ({len(node_sensors)}):")
        for s in node_sensors[:5]:
            print(f"  {s.get('name')}: {s.get('value')}")
