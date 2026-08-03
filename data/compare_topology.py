import json

# topology.json oku
with open('topology.json', 'r', encoding='utf-8') as f:
    topology = json.load(f)

print('=== topology.json icigi ===')
for node in topology:
    nid = node['node_id']
    ntype = node['type']
    loc = node['location']
    topic = node['topic']
    sensors = list(node['sensors'].keys())
    print(f'  {nid} ({ntype}) - {loc}')
    print(f'    topic: {topic}')
    print(f'    sensors: {sensors}')

# snapshot'taki node_id'leri cikar
with open('SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    snapshot = json.load(f)

all_node_ids = set()
for key, value in snapshot.items():
    if isinstance(value, dict):
        for n in value.get('nodes', []):
            all_node_ids.add(n['node_id'])
        for s in value.get('segments', []):
            all_node_ids.add(s['segment_id'])

print()
print('=== Karsilastirma ===')
for node in topology:
    nid = node['node_id']
    if nid in all_node_ids:
        print(f'  {nid} - ZATEN VAR')
    else:
        print(f'  {nid} - EKLENMELI (tip: {node["type"]})')
