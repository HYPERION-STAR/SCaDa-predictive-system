import json
import sys
sys.stdout.reconfigure(encoding='utf-8')

with open(r'C:\Users\HELIOS\Projects\SCaDa_predictive_maintenance\data\SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

top_regions = [k for k in data.keys() if k != 'meta']

# Check telemetry keys - are they all the same?
key_counts = {}
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        key_counts[tid] = key_counts.get(tid, 0) + 1

print(f"Unique telemetry keys: {len(key_counts)}")
from collections import Counter
kc = Counter(key_counts.values())
print(f"Key distribution: {dict(kc)}")

# Check if all keys in ic_anadolu are the same
ic_keys = list(data['ic_anadolu']['telemetry'].keys())
print(f"\nic_anadolu keys ({len(ic_keys)}):")
for k in ic_keys[:10]:
    print(f"  {k}")

# Check entity_ids
eid_counts = {}
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        eid = telem.get('entity_id', '')
        eid_counts[eid] = eid_counts.get(eid, 0) + 1

print(f"\nUnique entity_ids: {len(eid_counts)}")
ec = Counter(eid_counts.values())
print(f"Entity ID distribution: {dict(ec)}")

# Check how many entity_ids map to multiple telemetry keys
eid_to_keys = {}
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        eid = telem.get('entity_id', '')
        if eid not in eid_to_keys:
            eid_to_keys[eid] = set()
        eid_to_keys[eid].add(tid)

multi_map = {eid: keys for eid, keys in eid_to_keys.items() if len(keys) > 1}
print(f"\nEntity IDs with multiple telemetry keys: {len(multi_map)}")
for eid, keys in list(multi_map.items())[:5]:
    print(f"  {eid}: {keys}")
