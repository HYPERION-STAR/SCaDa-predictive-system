import json

# Mevcut snapshot'u oku
with open('SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

# topology.json'u oku
with open('topology.json', 'r', encoding='utf-8') as f:
    topology = json.load(f)

print("=== topology sensor metadata'sini tum entity'lere uygulama ===")

# Entity type -> topology sensor mapping
# topology'deki her node tipi, snapshot'taki entity_type'lara eslenir
entity_type_sensor_map = {
    # compressor entity'leri icin
    'compressor': {
        's_vibration_de': {'snapshot_key': 's_7_vibration_de_mm_s', 'min': 0.5, 'max': 5.5, 'unit': 'mm/s'},
        's_bearing_temp': {'snapshot_key': 's_10_bearing_temp_1_c', 'min': 45.0, 'max': 95.0, 'unit': 'C'},
        's_suction_pressure': {'snapshot_key': 's_1_suction_pressure_bar', 'min': 35.0, 'max': 50.0, 'unit': 'bar'},
    },
    # storage/ugs entity'leri icin
    'storage': {
        'tank_level_percent': {'snapshot_key': 's_storage_level_pct', 'min': 40.0, 'max': 98.0, 'unit': '%'},
        'injection_pressure': {'snapshot_key': 's_pressure_bar', 'min': 100.0, 'max': 150.0, 'unit': 'bar'},
    },
    # pump entity'leri icin
    'pump': {
        'p_cavitation_index': {'snapshot_key': 's_cavitation_index', 'min': 0.01, 'max': 0.15, 'unit': 'ratio'},
        'p_flow_rate': {'snapshot_key': 's_flow_rate_m3_h', 'min': 800.0, 'max': 1200.0, 'unit': 'm3/h'},
    },
    # pipeline_segment entity'leri icin
    'pipeline_segment': {
        'mass_imbalance': {'snapshot_key': 's_mass_imbalance_pct', 'min': 0.0, 'max': 2.5, 'unit': 'kg/s'},
        'gas_temperature': {'snapshot_key': 's_temperature_c', 'min': -5.0, 'max': 15.0, 'unit': 'C'},
    },
}

# Eslesme tablosu: topology type -> snapshot entity_type
type_mapping = {
    'compressor': ['compressor'],
    'storage': ['ugs', 'storage'],
    'pump': ['pump'],
    'pipeline_segment': ['pipeline_segment', 'segment'],
}

total_added = 0
total_entities = 0

for region_key, region_data in data.items():
    if not isinstance(region_data, dict) or 'telemetry' not in region_data:
        continue
    
    region_telemetry = region_data['telemetry']
    
    for tel_key, tel_data in list(region_telemetry.items()):
        etype = tel_data.get('entity_type', '')
        
        # Bu entity_type icin topology'de sensor tanimi var mi?
        topo_sensors = None
        for topo_type, snapshot_types in type_mapping.items():
            if etype in snapshot_types:
                topo_sensors = entity_type_sensor_map.get(topo_type, {})
                break
        
        if not topo_sensors:
            continue
        
        # Her sensor icin metadata ekle
        added_count = 0
        for topo_sensor_name, sensor_def in topo_sensors.items():
            snapshot_key = sensor_def['snapshot_key']
            if snapshot_key in tel_data:
                if 'sensor_metadata' not in tel_data:
                    tel_data['sensor_metadata'] = {}
                
                tel_data['sensor_metadata'][topo_sensor_name] = {
                    'snapshot_key': snapshot_key,
                    'min': sensor_def['min'],
                    'max': sensor_def['max'],
                    'unit': sensor_def['unit'],
                    'current_value': tel_data[snapshot_key]
                }
                added_count += 1
        
        if added_count > 0:
            total_added += added_count
            total_entities += 1

# Guncellenmis JSON'u kaydet
with open('SCaDa_live_snapshot.json', 'w', encoding='utf-8') as f:
    json.dump(data, f, indent=4, ensure_ascii=False)

print(f"\n=== TAMAMLANDI ===")
print(f"Toplam {total_entities} entity'e {total_added} sensor metadata eklendi!")

# Bolge bolge goster
for region_key, region_data in data.items():
    if not isinstance(region_data, dict) or 'telemetry' not in region_data:
        continue
    
    with_meta = 0
    for tel_key, tel_val in region_data['telemetry'].items():
        if 'sensor_metadata' in tel_val:
            with_meta += 1
    
    if with_meta > 0:
        print(f"  {region_key}: {with_meta}/{len(region_data['telemetry'])} entity'de sensor_metadata var")

# Ornek goster
print("\n=== ORNEK: cs_bursa_u1 (compressor) ===")
if 'marmara' in data and 'cs_bursa_u1' in data['marmara']['telemetry']:
    print(json.dumps(data['marmara']['telemetry']['cs_bursa_u1'].get('sensor_metadata', {}), indent=2, ensure_ascii=False))

print("\n=== ORNEK: ugs_tuzgolu_01 (ugs) ===")
if 'ic_anadolu' in data and 'ugs_tuzgolu_01' in data['ic_anadolu']['telemetry']:
    print(json.dumps(data['ic_anadolu']['telemetry']['ugs_tuzgolu_01'].get('sensor_metadata', {}), indent=2, ensure_ascii=False))

print("\n=== ORNEK: seg_izmir_aliaga (segment) ===")
if 'ege' in data and 'seg_izmir_aliaga' in data['ege']['telemetry']:
    print(json.dumps(data['ege']['telemetry']['seg_izmir_aliaga'].get('sensor_metadata', {}), indent=2, ensure_ascii=False))
