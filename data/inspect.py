import json
with open('SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

# Tum telemetry entity tiplerini ve ilk birinin anahtarlarini goster
entity_types = {}
for region_key, region_data in data.items():
    if isinstance(region_data, dict) and 'telemetry' in region_data:
        for tel_key, tel_val in region_data['telemetry'].items():
            etype = tel_val.get('entity_type', 'unknown')
            if etype not in entity_types:
                entity_types[etype] = {'count': 0, 'example': tel_key}
            entity_types[etype]['count'] += 1

print('Entity tipleri:')
for etype, info in entity_types.items():
    print(f'  {etype}: {info["count"]} adet, example: {info["example"]}')

# topology sensor tanimlarini goster
with open('topology.json', 'r', encoding='utf-8') as f:
    topology = json.load(f)

print('\ntopology sensor tanimlari:')
for node in topology:
    print(f'  {node["node_id"]} ({node["type"]}):')
    for sname, sdef in node['sensors'].items():
        print(f'    {sname}: min={sdef["min"]}, max={sdef["max"]}, unit={sdef["unit"]}')
