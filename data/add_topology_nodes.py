import json
import random

# Mevcut snapshot'u oku
with open('SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

# topology.json'u oku
with open('topology.json', 'r', encoding='utf-8') as f:
    topology = json.load(f)

random.seed(42)  # Tekrarlanabilir değerler

def gen_telemetry_compressor(entity_id):
    """Kompresör telemetry degerleri"""
    suction_p = round(random.uniform(45, 65), 1)
    discharge_p = round(suction_p * random.uniform(1.08, 1.15), 1)
    pressure_ratio = round(discharge_p / suction_p, 3) if suction_p > 0 else 0
    suction_t = round(random.uniform(-5, 20), 1)
    discharge_t = round(suction_t + random.uniform(20, 45), 1)
    rpm = random.randint(5200, 6000)
    vib_de = round(random.uniform(1.5, 5.5), 1)
    vib_nde = round(vib_de * random.uniform(0.8, 0.95), 1)
    axial_disp = round(random.uniform(0.8, 2.0), 2)
    bearing_t1 = round(suction_t + 50 + random.uniform(0, 15), 1)
    bearing_t2 = round(bearing_t1 - random.uniform(1, 3), 1)
    lube_p = round(random.uniform(2.0, 3.0), 1)
    lube_t = round(random.uniform(35, 48), 1)
    flow_meter = random.randint(200000, 900000)
    seal_p = round(random.uniform(3.5, 5.5), 1)
    gas_flow = int(flow_meter * random.uniform(0.97, 0.995))
    power = round(random.uniform(3.0, 10.5), 2)
    poly_eff = round(random.uniform(0.80, 0.90), 3)
    surge_margin = round(random.uniform(15, 45), 1)
    filter_dp = round(random.uniform(0.10, 0.60), 2)
    torque = random.randint(5000, 16000)
    ambient_t = round(suction_t + 10 + random.uniform(-2, 5), 1)
    inlet_p = suction_p
    flow_demand = int(flow_meter * random.uniform(1.0, 1.05))
    speed_sp = round(random.uniform(80, 98), 1)
    
    quality = "GOOD"
    if vib_de > 4.5 or surge_margin < 20:
        quality = "WARNING"
    if bearing_t1 > 80 or vib_de > 5.5:
        quality = "CRITICAL"
    
    return {
        "entity_id": entity_id,
        "entity_type": "compressor",
        "s_1_suction_pressure_bar": suction_p,
        "s_2_discharge_pressure_bar": discharge_p,
        "s_3_pressure_ratio": pressure_ratio,
        "s_4_suction_temp_c": suction_t,
        "s_5_discharge_temp_c": discharge_t,
        "s_6_shaft_rpm": rpm,
        "s_7_vibration_de_mm_s": vib_de,
        "s_8_vibration_nde_mm_s": vib_nde,
        "s_9_axial_displacement_mm": axial_disp,
        "s_10_bearing_temp_1_c": bearing_t1,
        "s_11_bearing_temp_2_c": bearing_t2,
        "s_12_lube_oil_pressure_bar": lube_p,
        "s_13_lube_oil_temp_c": lube_t,
        "s_14_gas_flow_meter_m3_h": flow_meter,
        "s_15_seal_gas_pressure_bar": seal_p,
        "s_16_gas_flow_m3_h": gas_flow,
        "s_17_power_mw": power,
        "s_18_polytropic_efficiency": poly_eff,
        "s_19_surge_margin_pct": surge_margin,
        "s_20_filter_dp_bar": filter_dp,
        "s_21_torque_nm": torque,
        "op_1_ambient_temp_c": ambient_t,
        "op_2_inlet_pressure_bar": inlet_p,
        "op_3_flow_demand_m3_h": flow_demand,
        "op_4_speed_setpoint_pct": speed_sp,
        "quality": quality
    }

def gen_telemetry_ugs(entity_id):
    """UGS telemetry degerleri"""
    storage_level = round(random.uniform(40, 98), 1)
    inventory = random.randint(5000, 15000)
    injection_flow = random.randint(100000, 500000)
    withdrawal_flow = random.randint(50000, 300000)
    pressure = round(random.uniform(40, 80), 1)
    cavern_t = round(random.uniform(30, 65), 1)
    daily_cap = random.randint(8000, 20000)
    
    return {
        "entity_id": entity_id,
        "entity_type": "ugs",
        "s_storage_level_pct": storage_level,
        "s_inventory_mcm": inventory,
        "s_injection_flow_m3_h": injection_flow,
        "s_withdrawal_flow_m3_h": withdrawal_flow,
        "s_pressure_bar": pressure,
        "s_cavern_temp_c": cavern_t,
        "s_daily_capacity_mcm_day": daily_cap,
        "s_working_capacity_pct": storage_level,
        "quality": "GOOD"
    }

def gen_telemetry_pump(entity_id):
    """Pompa telemetry degerleri"""
    cavitation = round(random.uniform(0.01, 0.15), 3)
    flow_rate = random.randint(800, 1200)
    suction_p = round(random.uniform(3.0, 8.0), 1)
    discharge_p = round(suction_p * random.uniform(1.5, 3.0), 1)
    vibration = round(random.uniform(1.0, 4.5), 1)
    bearing_t = round(random.uniform(40, 75), 1)
    motor_current = round(random.uniform(25, 65), 1)
    motor_temp = round(random.uniform(45, 85), 1)
    seal_temp = round(random.uniform(30, 60), 1)
    efficiency = round(random.uniform(0.70, 0.92), 3)
    
    quality = "GOOD"
    if cavitation > 0.10 or bearing_t > 70:
        quality = "WARNING"
    
    return {
        "entity_id": entity_id,
        "entity_type": "pump",
        "s_cavitation_index": cavitation,
        "s_flow_rate_m3_h": flow_rate,
        "s_suction_pressure_bar": suction_p,
        "s_discharge_pressure_bar": discharge_p,
        "s_vibration_mm_s": vibration,
        "s_bearing_temp_c": bearing_t,
        "s_motor_current_a": motor_current,
        "s_motor_temp_c": motor_temp,
        "s_seal_temp_c": seal_temp,
        "s_efficiency": efficiency,
        "quality": quality
    }

def gen_telemetry_pipeline(entity_id):
    """Boru hatti telemetry degerleri"""
    inlet_p = round(random.uniform(30, 70), 1)
    outlet_p = round(inlet_p * random.uniform(0.85, 0.98), 1)
    dp = round(inlet_p - outlet_p, 1)
    flow = random.randint(100000, 600000)
    outflow = int(flow * random.uniform(0.97, 0.995))
    mass_imb = round(abs(flow - outflow) / flow * 100, 2)
    temp = round(random.uniform(-5, 25), 1)
    cathodic_v = round(random.uniform(10.0, 14.0), 1)
    
    quality = "GOOD"
    if mass_imb > 2.0:
        quality = "WARNING"
    
    return {
        "entity_id": entity_id,
        "entity_type": "pipeline_segment",
        "s_inlet_pressure_bar": inlet_p,
        "s_outlet_pressure_bar": outlet_p,
        "s_dp_bar": dp,
        "s_flow_m3_h": flow,
        "s_outflow_m3_h": outflow,
        "s_mass_imbalance_pct": mass_imb,
        "s_temperature_c": temp,
        "s_cathodic_protection_v": cathodic_v,
        "quality": quality
    }

# topology.json'daki 4 node'u snapshot'a ekle
added_count = 0

for topo_node in topology:
    nid = topo_node['node_id']
    ntype = topo_node['type']
    location = topo_node['location']
    sensors = topo_node['sensors']
    
    print(f"\nIsleniyor: {nid} ({ntype}) - {location}")
    
    if nid == "CS-ISTANBUL":
        # marmara bolgesine ekle
        if 'marmara' not in data:
            data['marmara'] = {"label": "Marmara Bolgesi", "nodes": [], "segments": [], "equipment": [], "telemetry": {}}
        
        # Node ekle
        node_entry = {
            "node_id": nid,
            "name": f"{location} Kompresor Istasyonu",
            "node_type": "CS",
            "lat": 41.0050,
            "lon": 28.9700,
            "elevation_m": 40,
            "pipeline_ids": ["DOGALGAZ_ANA"],
            "design_pressure_bar": 64,
            "capacity_mcm_day": 20000,
            "region": "marmara",
            "equipment_count": 2
        }
        data['marmara']['nodes'].append(node_entry)
        
        # Equipment ekle
        equip1 = {
            "unit_id": f"{nid}-U1",
            "station_node_id": nid,
            "unit_type": "compressor",
            "model": "Siemens SG5000",
            "rated_power_mw": 7.2,
            "install_date": "2016-03-15",
            "design_pressure_bar": 64,
            "flow_capacity_m3_h": 700000
        }
        equip2 = {
            "unit_id": f"{nid}-U2",
            "station_node_id": nid,
            "unit_type": "compressor",
            "model": "Siemens SG5000",
            "rated_power_mw": 7.2,
            "install_date": "2016-03-15",
            "design_pressure_bar": 64,
            "flow_capacity_m3_h": 700000
        }
        data['marmara']['equipment'].extend([equip1, equip2])
        
        # Telemetry ekle
        data['marmara']['telemetry'][f"{nid.lower()}_u1"] = gen_telemetry_compressor(f"{nid}-U1")
        data['marmara']['telemetry'][f"{nid.lower()}_u2"] = gen_telemetry_compressor(f"{nid}-U2")
        
        added_count += 1
        print(f"  -> marmara bolgesine eklendi (node + 2 equipment + 2 telemetry)")
    
    elif nid == "UGS-SILIVRI":
        # marmara bolgesine ekle
        if 'marmara' not in data:
            data['marmara'] = {"label": "Marmara Bolgesi", "nodes": [], "segments": [], "equipment": [], "telemetry": {}}
        
        node_entry = {
            "node_id": nid,
            "name": f"{location} Yeralti Gaz Depolama",
            "node_type": "UGS",
            "lat": 41.1500,
            "lon": 28.3500,
            "elevation_m": 80,
            "pipeline_ids": ["DOGALGAZ_ANA"],
            "design_pressure_bar": 80,
            "capacity_mcm_day": 12000,
            "region": "marmara"
        }
        data['marmara']['nodes'].append(node_entry)
        
        # Telemetry ekle
        data['marmara']['telemetry'][nid.lower()] = gen_telemetry_ugs(nid)
        
        added_count += 1
        print(f"  -> marmara bolgesine eklendi (node + 1 telemetry)")
    
    elif nid == "PS-IZMIR":
        # ege bolgesine ekle
        if 'ege' not in data:
            data['ege'] = {"label": "Ege Bolgesi", "nodes": [], "segments": [], "equipment": [], "telemetry": {}}
        
        node_entry = {
            "node_id": nid,
            "name": f"{location} Pompa Istasyonu",
            "node_type": "PS",
            "lat": 38.4150,
            "lon": 27.1300,
            "elevation_m": 20,
            "pipeline_ids": ["DOGALGAZ_ANA"],
            "design_pressure_bar": 40,
            "capacity_mcm_day": 5000,
            "region": "ege",
            "equipment_count": 1
        }
        data['ege']['nodes'].append(node_entry)
        
        # Equipment ekle
        equip1 = {
            "unit_id": f"{nid}-U1",
            "station_node_id": nid,
            "unit_type": "pump",
            "model": "Grundfos NM95",
            "rated_power_mw": 2.5,
            "install_date": "2017-06-20",
            "design_pressure_bar": 40,
            "flow_capacity_m3_h": 1200
        }
        data['ege']['equipment'].append(equip1)
        
        # Telemetry ekle
        data['ege']['telemetry'][nid.lower()] = gen_telemetry_pump(nid)
        
        added_count += 1
        print(f"  -> ege bolgesine eklendi (node + 1 equipment + 1 telemetry)")
    
    elif nid == "PIPE-SIVAS-ERZINCAN":
        # ic_anadolu bolgesine ekle (Sivas-Icer Anadolu baglantisi)
        if 'ic_anadolu' not in data:
            data['ic_anadolu'] = {"label": "Ic Anadolu Bolgesi", "nodes": [], "segments": [], "equipment": [], "telemetry": {}}
        
        segment_entry = {
            "segment_id": nid,
            "from_node": "OFFTAKE-SIVAS-01",
            "to_node": "JUNCTION-ACIHUYUK-01",
            "pipeline_id": "DOGALGAZ_ANA",
            "product": "gas",
            "length_km": 245.8,
            "diameter_inch": 36,
            "wall_thickness_mm": 11.0,
            "design_pressure_bar": 64,
            "flow_direction": "BIDIRECTIONAL",
            "max_capacity_mcm_day": 15000,
            "geometry_polyline": [[39.75, 37.02], [39.50, 36.50], [39.20, 35.80], [38.95, 35.00], [38.95, 32.55]]
        }
        data['ic_anadolu']['segments'].append(segment_entry)
        
        # Telemetry ekle
        data['ic_anadolu']['telemetry'][nid.lower()] = gen_telemetry_pipeline(nid)
        
        added_count += 1
        print(f"  -> ic_anadolu bolgesine eklendi (segment + 1 telemetry)")

# Guncellenmis JSON'u kaydet
with open('SCaDa_live_snapshot.json', 'w', encoding='utf-8') as f:
    json.dump(data, f, indent=4, ensure_ascii=False)

print(f"\n=== TAMAMLANDI ===")
print(f"topology.json'dan {added_count} node basariyla eklendi!")

# Istatistikleri goster
total_nodes = 0
total_segments = 0
total_equipment = 0
total_telemetry = 0

for key, value in data.items():
    if isinstance(value, dict):
        nodes = len(value.get('nodes', []))
        segments = len(value.get('segments', []))
        equipment = len(value.get('equipment', []))
        telemetry = len(value.get('telemetry', {}))
        total_nodes += nodes
        total_segments += segments
        total_equipment += equipment
        total_telemetry += telemetry
        print(f"  {key}: {nodes} nodes, {segments} segments, {equipment} equipment, {telemetry} telemetry")

print(f"\nTOPLAM: {total_nodes} nodes, {total_segments} segments, {total_equipment} equipment, {total_telemetry} telemetry")
