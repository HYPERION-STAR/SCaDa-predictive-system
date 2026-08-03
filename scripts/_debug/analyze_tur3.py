import json
import sys
sys.stdout.reconfigure(encoding='utf-8')

with open(r'C:\Users\HELIOS\Projects\SCaDa_predictive_maintenance\data\SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

top_regions = [k for k in data.keys() if k != 'meta']

print("=== K1 — META ===")
print(f"  meta: nodes={data['meta']['total_nodes']}, segs={data['meta']['total_segments']}, eq={data['meta']['total_equipment']}, tags={data['meta']['total_telemetry_tags']}")

real_nodes = sum(len(data[r].get('nodes', [])) for r in top_regions)
real_segs = sum(len(data[r].get('segments', [])) for r in top_regions)
real_eq = sum(len(data[r].get('equipment', [])) for r in top_regions)

# Count actual tags (sensor-level, not entity-level)
total_tags = 0
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        et = telem.get('entity_type', '')
        if et == 'compressor': total_tags += 25
        elif et == 'segment': total_tags += 9
        elif et == 'border_entry': total_tags += 5
        elif et in ('ugs','fsru','lng_terminal'): total_tags += 8
        else: total_tags += len(telem)

print(f"  gercek: nodes={real_nodes}, segs={real_segs}, eq={real_eq}, tags={total_tags}")
print(f"  equipment fark: meta={data['meta']['total_equipment']} gercek={real_eq}")
print(f"  tag fark: meta={data['meta']['total_telemetry_tags']} gercek={total_tags}")

# Check if tag count should be entity count instead
entity_count = sum(len(data[r].get('telemetry', {})) for r in top_regions)
print(f"  entity_count (varlik sayisi): {entity_count}")

# Check equipment status field
eq_with_status = 0
for rk in top_regions:
    for eq in data[rk].get('equipment', []):
        if 'status' in eq:
            eq_with_status += 1
print(f"\n=== K5 — Equipment status ===")
print(f"  Equipment with status: {eq_with_status}/{real_eq}")

# Check K2 issues
print(f"\n=== K2 — ID Issues ===")
# NAHCIivan
nahciivan = []
for rk in top_regions:
    for node in data[rk].get('nodes', []):
        if 'NAHCIivan' in node['node_id']:
            nahciivan.append((rk, node['node_id']))
    for seg in data[rk].get('segments', []):
        if 'NAHCIivan' in seg['segment_id']:
            nahciivan.append((rk, seg['segment_id']))
print(f"  NAHCIivan occurrences: {len(nahciivan)}")
for rk, nid in nahciivan:
    print(f"    {rk}/{nid}")

# TAP- prefix
tap_prefix = []
for rk in top_regions:
    for eq in data[rk].get('equipment', []):
        if eq['unit_id'].startswith('TAP-'):
            tap_prefix.append((rk, eq['unit_id']))
print(f"  TAP- prefix equipment: {len(tap_prefix)}")
for rk, uid in tap_prefix:
    print(f"    {rk}/{uid}")

# K4 — mass_imbalance check
print(f"\n=== K4 — Mass Imbalance ===")
imb_mismatch = 0
pressure_mono = 0
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        if telem.get('entity_type') != 'segment':
            continue
        s_flow = telem.get('s_flow_m3_h', 0)
        s_outflow = telem.get('s_outflow_m3_h', 0)
        imb_beyan = telem.get('s_mass_imbalance_pct', 0)
        
        if s_flow > 0:
            imb_hesap = round((s_flow - s_outflow) / s_flow * 100, 2)
            if abs(imb_beyan - imb_hesap) > 0.1:
                imb_mismatch += 1
        
        p_in = telem.get('s_inlet_pressure_bar', 0)
        p_out = telem.get('s_outlet_pressure_bar', 0)
        if p_in <= p_out and s_flow > 0:
            pressure_mono += 1

print(f"  Mass imbalance mismatch: {imb_mismatch}")
print(f"  Pressure monotonicity violation: {pressure_mono}")
