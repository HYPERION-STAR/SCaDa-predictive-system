"""
Denetim Turu 3 — Kalan Düzeltmeler (Düzeltilmiş)
K2: NAHCIivan → NAHCIVAN + TAP- prefix → CS-TAP-
K5: Equipment status hizalama
"""
import json
import sys
sys.stdout.reconfigure(encoding='utf-8')

with open(r'C:\Users\HELIOS\Projects\SCaDa_predictive_maintenance\data\SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

top_regions = [k for k in data.keys() if k != 'meta']
fixes = []

# ════════════════════════════════════════════════════════════════════════════
# K2 — NAHCIivan → NAHCIVAN (yazım hatası)
# ════════════════════════════════════════════════════════════════════════════

for rk in top_regions:
    region = data[rk]
    
    # Fix node IDs
    for node in region.get('nodes', []):
        old = node['node_id']
        new = old.replace('NAHCIivan', 'NAHCIVAN')
        if new != old:
            fixes.append(f"K2 Node: {old} → {new}")
            node['node_id'] = new
    
    # Fix segment IDs
    for seg in region.get('segments', []):
        old = seg['segment_id']
        new = old.replace('NAHCIivan', 'NAHCIVAN')
        if new != old:
            fixes.append(f"K2 Segment: {old} → {new}")
            seg['segment_id'] = new

# ════════════════════════════════════════════════════════════════════════════
# K2 — TAP- prefix → CS-TAP- (regex'e uyumlu)
# ════════════════════════════════════════════════════════════════════════════

for rk in top_regions:
    region = data[rk]
    
    for eq in region.get('equipment', []):
        old = eq['unit_id']
        if old.startswith('TAP-'):
            new = 'CS-' + old
            fixes.append(f"K2 Equipment: {old} → {new}")
            eq['unit_id'] = new

# ════════════════════════════════════════════════════════════════════════════
# K3 — Telemetry entity_id ve key'leri düzelt (NAHCIivan + TAP- sonrası)
# ════════════════════════════════════════════════════════════════════════════

for rk in top_regions:
    region = data[rk]
    
    # Build new telemetry dict with correct keys
    new_telemetry = {}
    for tid, telem in region.get('telemetry', {}).items():
        eid = telem.get('entity_id', '')
        
        # Normalize entity_id
        new_eid = eid.replace('NAHCIivan', 'NAHCIVAN')
        if new_eid.startswith('TAP-'):
            new_eid = 'CS-' + new_eid
        
        if new_eid != eid:
            fixes.append(f"K3 entity_id: {eid} → {new_eid}")
            telem['entity_id'] = new_eid
        
        # Compute correct key from entity_id
        correct_key = new_eid.replace('-', '_').lower()
        
        if tid != correct_key:
            fixes.append(f"K3 key: {tid} → {correct_key}")
        
        new_telemetry[correct_key] = telem
    
    region['telemetry'] = new_telemetry

# ════════════════════════════════════════════════════════════════════════════
# K5 — Equipment'lere status alanı ekle (telemetriyle hizala)
# ════════════════════════════════════════════════════════════════════════════

# Build telemetry status lookup
telem_status = {}
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        if telem.get('entity_type') == 'compressor':
            eid = telem.get('entity_id', '')
            status = telem.get('status', 'running')
            telem_status[eid] = status

for rk in top_regions:
    region = data[rk]
    
    for eq in region.get('equipment', []):
        uid = eq['unit_id']
        if uid in telem_status:
            old_status = eq.get('status', None)
            new_status = telem_status[uid]
            if old_status != new_status:
                fixes.append(f"K5 Equipment status: {uid} {old_status} → {new_status}")
                eq['status'] = new_status

# ════════════════════════════════════════════════════════════════════════════
# K1 — Meta'yı yeniden hesapla
# ════════════════════════════════════════════════════════════════════════════

real_nodes = sum(len(data[r].get('nodes', [])) for r in top_regions)
real_segs = sum(len(data[r].get('segments', [])) for r in top_regions)
real_eq = sum(len(data[r].get('equipment', [])) for r in top_regions)

total_tags = 0
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        et = telem.get('entity_type', '')
        if et == 'compressor': total_tags += 25
        elif et == 'segment': total_tags += 9
        elif et == 'border_entry': total_tags += 5
        elif et in ('ugs','fsru','lng_terminal'): total_tags += 8
        else: total_tags += len(telem)

for key, val in [('total_nodes', real_nodes), ('total_segments', real_segs),
                 ('total_equipment', real_eq), ('total_telemetry_tags', total_tags)]:
    old_val = data['meta'].get(key)
    if old_val != val:
        fixes.append(f"K1 {key}: {old_val} → {val}")

data['meta']['total_nodes'] = real_nodes
data['meta']['total_segments'] = real_segs
data['meta']['total_equipment'] = real_eq
data['meta']['total_telemetry_tags'] = total_tags

# ════════════════════════════════════════════════════════════════════════════
# Write
# ════════════════════════════════════════════════════════════════════════════

with open(r'C:\Users\HELIOS\Projects\SCaDa_predictive_maintenance\data\SCaDa_live_snapshot.json', 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=4)

print(f"Toplam {len(fixes)} düzeltme:")
for fix in fixes:
    print(f"  {fix}")

# Final summary
print(f"\n=== SON DURUM ===")
print(f"  Nodes: {real_nodes}")
print(f"  Segments: {real_segs}")
print(f"  Equipment: {real_eq}")
print(f"  Telemetry tags: {total_tags}")
