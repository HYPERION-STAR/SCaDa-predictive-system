import json

# Mevcut snapshot'u oku
with open('SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

# topology.json'u oku
with open('topology.json', 'r', encoding='utf-8') as f:
    topology = json.load(f)

print("=== topology sensor metadata'sini snapshot telemetry'ye ekleme ===")

# Sensor eslesme tablosu: topology sensor adi -> snapshot telemetry key
sensor_mapping = {
    # CS-ISTANBUL (compressor)
    's_vibration_de': 's_7_vibration_de_mm_s',
    's_bearing_temp': 's_10_bearing_temp_1_c',
    's_suction_pressure': 's_1_suction_pressure_bar',
    
    # UGS-SILIVRI (storage)
    'tank_level_percent': 's_storage_level_pct',
    'injection_pressure': 's_pressure_bar',
    
    # PS-IZMIR (pump)
    'p_cavitation_index': 's_cavitation_index',
    'p_flow_rate': 's_flow_rate_m3_h',
    
    # PIPE-SIVAS-ERZINCAN (pipeline_segment)
    'mass_imbalance': 's_mass_imbalance_pct',
    'gas_temperature': 's_temperature_c',
}

# Bolge -> telemetry prefix eslesmesi
region_telemetry_map = {
    'CS-ISTANBUL': {'region': 'marmara', 'prefix': 'cs-istanbul'},
    'UGS-SILIVRI': {'region': 'marmara', 'prefix': 'ugs-silivri'},
    'PS-IZMIR': {'region': 'ege', 'prefix': 'ps-izmir'},
    'PIPE-SIVAS-ERZINCAN': {'region': 'ic_anadolu', 'prefix': 'pipe-sivas'},
}

total_added = 0

for topo_node in topology:
    nid = topo_node['node_id']
    ntype = topo_node['type']
    sensors_def = topo_node['sensors']
    
    if nid not in region_telemetry_map:
        print(f"  {nid}: Bolge haritasi yok, atlandi")
        continue
    
    config = region_telemetry_map[nid]
    region = config['region']
    prefix = config['prefix']
    
    print(f"\n  Isleme: {nid} ({ntype}) -> {region}.{prefix}")
    
    if region not in data:
        print(f"    -> Bolge bulunamadi, atlandi")
        continue
    
    region_telemetry = data[region]['telemetry']
    
    # Bu node'a ait telemetry entity'lerini bul
    matched_entities = {k: v for k, v in region_telemetry.items() if k.startswith(prefix)}
    
    if not matched_entities:
        print(f"    -> Ilgili telemetry entity'si bulunamadi")
        continue
    
    for tel_key, tel_data in matched_entities.items():
        # Her sensor icin metadata ekle
        added_sensors = []
        for topo_sensor_name, snapshot_key in sensor_mapping.items():
            if snapshot_key in tel_data:
                # Bu sensor zaten var, metadata ekle
                if 'sensor_metadata' not in tel_data:
                    tel_data['sensor_metadata'] = {}
                
                topo_sensor_def = sensors_def.get(topo_sensor_name, {})
                tel_data['sensor_metadata'][topo_sensor_name] = {
                    'snapshot_key': snapshot_key,
                    'min': topo_sensor_def.get('min'),
                    'max': topo_sensor_def.get('max'),
                    'unit': topo_sensor_def.get('unit'),
                    'current_value': tel_data[snapshot_key]
                }
                added_sensors.append(topo_sensor_name)
        
        if added_sensors:
            total_added += len(added_sensors)
            print(f"    {tel_key}: {len(added_sensors)} sensor metadata eklendi ({', '.join(added_sensors)})")

# Guncellenmis JSON'u kaydet
with open('SCaDa_live_snapshot.json', 'w', encoding='utf-8') as f:
    json.dump(data, f, indent=4, ensure_ascii=False)

print(f"\n=== TAMAMLANDI ===")
print(f"Toplam {total_added} sensor metadata eklendi!")

# Bir ornek goster
print("\n=== ORNEK: cs-istanbul_u1 sensor_metadata ===")
if 'marmara' in data and 'cs-istanbul_u1' in data['marmara']['telemetry']:
    print(json.dumps(data['marmara']['telemetry']['cs-istanbul_u1'].get('sensor_metadata', {}), indent=2, ensure_ascii=False))
