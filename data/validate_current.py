import json
import re
from collections import Counter

with open('SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

print("=" * 70)
print("DOGRULAMA RAPORU - SCaDa_live_snapshot.json")
print("=" * 70)

# ============================================================
# H1: meta içerik uyuşmazligi
# ============================================================
print("\n--- H1: meta icerik uyuşmazligi ---")
if 'meta' in data and isinstance(data['meta'], dict):
    meta = data['meta']
    print(f"  meta'da yazan: nodes={meta.get('total_nodes', 'N/A')}, segments={meta.get('total_segments', 'N/A')}, equipment={meta.get('total_equipment', 'N/A')}, telemetry_tags={meta.get('total_telemetry_tags', 'N/A')}")
    
    # Gerçek sayim
    real_nodes = 0
    real_segments = 0
    real_equipment = 0
    real_telemetry_entities = 0
    real_telemetry_tags = 0
    
    for key, val in data.items():
        if isinstance(val, dict):
            real_nodes += len(val.get('nodes', []))
            real_segments += len(val.get('segments', []))
            real_equipment += len(val.get('equipment', []))
            tel = val.get('telemetry', {})
            real_telemetry_entities += len(tel)
            for tv in tel.values():
                if isinstance(tv, dict):
                    real_telemetry_tags += len([k for k in tv.keys() if not k.startswith('sensor_')])
    
    print(f"  Gerçek: nodes={real_nodes}, segments={real_segments}, equipment={real_equipment}, telemetry_entities={real_telemetry_entities}, telemetry_tags={real_telemetry_tags}")
    
    if meta.get('total_nodes') != real_nodes:
        print(f"  [ERROR] total_nodes: meta={meta.get('total_nodes')} != gerçek={real_nodes}")
    if meta.get('total_segments') != real_segments:
        print(f"  [ERROR] total_segments: meta={meta.get('total_segments')} != gerçek={real_segments}")
    if meta.get('total_equipment') != real_equipment:
        print(f"  [ERROR] total_equipment: meta={meta.get('total_equipment')} != gerçek={real_equipment}")
    if meta.get('total_telemetry_tags') != real_telemetry_tags:
        print(f"  [ERROR] total_telemetry_tags: meta={meta.get('total_telemetry_tags')} != gerçek={real_telemetry_tags}")
else:
    print("  [WARN] meta yok veya boş")

# ============================================================
# H2: Mevsim celşkisi (ambient temp)
# ============================================================
print("\n--- H2: Mevsim celşkisi ---")
if 'meta' in data and isinstance(data['meta'], dict):
    season = data['meta'].get('season_profile', 'N/A')
    meta_ambient = data['meta'].get('ambient_temp_c', 'N/A')
    print(f"  season_profile={season}, meta ambient_temp_c={meta_ambient}")

all_ambient = []
for key, val in data.items():
    if isinstance(val, dict):
        for tel_key, tel_val in val.get('telemetry', {}).items():
            if isinstance(tel_val, dict) and 'op_1_ambient_temp_c' in tel_val:
                all_ambient.append((key, tel_key, tel_val['op_1_ambient_temp_c']))

if all_ambient:
    temps = [t[2] for t in all_ambient]
    print(f"  Ambient temp araligi: [{min(temps)}, {max(temps)}]°C")
    print(f"  Tüm ambient degerleri:")
    for region, tel_key, temp in sorted(all_ambient, key=lambda x: x[2]):
        print(f"    {region}.{tel_key}: {temp}°C")
    if min(temps) < -5 and max(temps) > 25:
        print(f"  [ERROR] Çok geniş aralik: {min(temps)}°C ile {max(temps)}°C — tek anda olamaz")

# ============================================================
# H3: Ölü varliklar (telemetrisi olmayan)
# ============================================================
print("\n--- H3: Ölü varliklar ---")
dead_nodes = []
dead_segments = []
dead_equipment = []

for key, val in data.items():
    if not isinstance(val, dict):
        continue
    
    # Var olan entity_id'leri topla
    telemetry_entity_ids = set()
    for tel_key, tel_val in val.get('telemetry', {}).items():
        if isinstance(tel_val, dict) and 'entity_id' in tel_val:
            telemetry_entity_ids.add(tel_val['entity_id'])
    
    # Node'lari kontrol et
    for node in val.get('nodes', []):
        nid = node['node_id']
        ntype = node.get('node_type', '')
        if ntype in ('CS', 'PS'):
            # CS ve PS'lerin telemetrisi olmalı
            matching = [eid for eid in telemetry_entity_ids if nid.upper().replace('-', '_') in eid.lower() or nid in eid]
            if not matching:
                dead_nodes.append((key, nid, ntype))
    
    # Segment kontrolü
    for seg in val.get('segments', []):
        sid = seg['segment_id']
        matching = [eid for eid in telemetry_entity_ids if sid.upper().replace('-', '_') in eid.lower() or sid in eid]
        if not matching:
            dead_segments.append((key, sid))
    
    # Equipment kontrolü
    for equip in val.get('equipment', []):
        uid = equip['unit_id']
        matching = [eid for eid in telemetry_entity_ids if uid.upper().replace('-', '_') in eid.lower() or uid in eid]
        if not matching:
            dead_equipment.append((key, uid))

print(f"  Telemetrisi olmayan CS/PS node'lari: {len(dead_nodes)}")
for region, nid, ntype in dead_nodes[:10]:
    print(f"    {region}.{nid} ({ntype})")
if len(dead_nodes) > 10:
    print(f"    ... ve {len(dead_nodes)-10} tane daha")

print(f"  Telemetrisi olmayan segment'ler: {len(dead_segments)}")
for region, sid in dead_segments[:10]:
    print(f"    {region}.{sid}")
if len(dead_segments) > 10:
    print(f"    ... ve {len(dead_segments)-10} tane daha")

print(f"  Telemetrisi olmayan equipment'ler: {len(dead_equipment)}")
for region, uid in dead_equipment[:10]:
    print(f"    {region}.{uid}")
if len(dead_equipment) > 10:
    print(f"    ... ve {len(dead_equipment)-10} tane daha")

# ============================================================
# H4: Çift regions anahtari
# ============================================================
print("\n--- H4: Çift regions anahtari ---")
top_level_regions = [k for k in data.keys() if isinstance(data[k], dict) and 'nodes' in data[k]]
regions_in_meta = []
if 'regions' in data and isinstance(data['regions'], dict):
    regions_in_meta = list(data['regions'].keys()) if isinstance(data['regions'], dict) else []

print(f"  Top-level bölgeler: {len(top_level_regions)}")
for r in top_level_regions:
    print(f"    {r}")
if regions_in_meta:
    print(f"  regions altinda bölgeler: {len(regions_in_meta)}")
    for r in regions_in_meta:
        print(f"    {r}")
    print(f"  [ERROR] Çift regions yapisi!")

# ============================================================
# H5: ID bozuk / tutarsizlik
# ============================================================
print("\n--- H5: ID bozuk / tutarsizlik ---")

# Turkce karakter normalizasyonu
turkce_fixes = {
    'TUZGOELU': ('TUZGOLU', 'TUZ GÖLÜ'),
    'ACIHHUYUK': ('ACIHUYUK', 'ACI HÜYÜK'),
    'NAHCIIVAN': ('NAHCIVAN', 'NAÇİVAN'),
    'DURU': ('DURU', None),  # kontrol et
}

all_node_ids = set()
all_segment_ids = set()
all_equipment_ids = set()
all_entity_ids = set()
telemetry_keys = set()

for key, val in data.items():
    if not isinstance(val, dict):
        continue
    
    for node in val.get('nodes', []):
        nid = node['node_id']
        all_node_ids.add(nid)
        for fix_old, fix_new, fix_name in turkce_fixes.values():
            if fix_old in nid:
                print(f"  [WARN] Turkce karakter: {nid} -> {nid.replace(fix_old, fix_new)} ({fix_name})")
    
    for seg in val.get('segments', []):
        sid = seg['segment_id']
        all_segment_ids.add(sid)
        for fix_old, fix_new, fix_name in turkce_fixes.values():
            if fix_old in sid:
                print(f"  [WARN] Turkce karakter (segment): {sid} -> {sid.replace(fix_old, fix_new)} ({fix_name})")
    
    for equip in val.get('equipment', []):
        uid = equip['unit_id']
        all_equipment_ids.add(uid)
    
    for tel_key, tel_val in val.get('telemetry', {}).items():
        if isinstance(tel_val, dict):
            telemetry_keys.add(tel_key)
            eid = tel_val.get('entity_id', '')
            all_entity_ids.add(eid)

# Telemetry anahtari <-> entity_id uyuşmazligi
print("\n  Telemetry anahtari <-> entity_id uyuşmazliklari:")
mismatch_count = 0
for key, val in data.items():
    if not isinstance(val, dict):
        continue
    for tel_key, tel_val in val.get('telemetry', {}).items():
        if isinstance(tel_val, dict):
            eid = tel_val.get('entity_id', '')
            # Normalizasyon: entity_id'deki tire ve buyuk harfleri telemetry anahtari formatina cevir
            expected_key = eid.upper().replace('-', '_').lower()
            if tel_key != expected_key:
                mismatch_count += 1
                print(f"    {key}.{tel_key} -> entity_id={eid}, beklenen={expected_key}")

print(f"  Toplam uyuşmazlik: {mismatch_count}")

# ID tekrar kontrolü
node_id_counts = Counter(all_node_ids)
seg_id_counts = Counter(all_segment_ids)
equip_id_counts = Counter(all_equipment_ids)

for nid, count in node_id_counts.items():
    if count > 1:
        print(f"  [ERROR] Tekrarlanan node_id: {nid} ({count} kez)")
for sid, count in seg_id_counts.items():
    if count > 1:
        print(f"  [ERROR] Tekrarlanan segment_id: {sid} ({count} kez)")
for uid, count in equip_id_counts.items():
    if count > 1:
        print(f"  [ERROR] Tekrarlanan unit_id: {uid} ({count} kez)")

# ============================================================
# H6: entity_type tutarsizlik
# ============================================================
print("\n--- H6: entity_type tutarsizlik ---")
entity_type_counts = Counter()
for key, val in data.items():
    if not isinstance(val, dict):
        continue
    for tel_key, tel_val in val.get('telemetry', {}).items():
        if isinstance(tel_val, dict):
            etype = tel_val.get('entity_type', 'unknown')
            entity_type_counts[etype] += 1

print(f"  entity_type dagilimi:")
for etype, count in entity_type_counts.most_common():
    print(f"    {etype}: {count}")

if 'segment' in entity_type_counts and 'pipeline_segment' in entity_type_counts:
    print(f"  [ERROR] entity_type iki deger: 'segment'({entity_type_counts['segment']}) ve 'pipeline_segment'({entity_type_counts['pipeline_segment']})")

# ============================================================
# H7: Fiziksel makullük (basit kontroller)
# ============================================================
print("\n--- H7: Fiziksel makullük (basit) ---")
physics_errors = 0
for key, val in data.items():
    if not isinstance(val, dict):
        continue
    for tel_key, tel_val in val.get('telemetry', {}).items():
        if isinstance(tel_val, dict) and tel_val.get('entity_type') == 'compressor':
            suction_p = tel_val.get('s_1_suction_pressure_bar', 0)
            discharge_p = tel_val.get('s_2_discharge_pressure_bar', 0)
            pressure_ratio = tel_val.get('s_3_pressure_ratio', 0)
            
            # Discharge > Suction olmalı
            if suction_p > 0 and discharge_p > 0 and discharge_p <= suction_p:
                physics_errors += 1
                print(f"    [ERROR] {tel_key}: discharge_p({discharge_p}) <= suction_p({suction_p})")
            
            # Pressure ratio kontrolü
            if suction_p > 0:
                expected_ratio = round(discharge_p / suction_p, 3)
                if abs(pressure_ratio - expected_ratio) > 0.01:
                    physics_errors += 1
                    print(f"    [WARN] {tel_key}: pressure_ratio={pressure_ratio}, hesaplanan={expected_ratio}")

print(f"  Fiziksel hata/uysuzluk: {physics_errors}")

# ============================================================
# ÖZET
# ============================================================
print("\n" + "=" * 70)
print("OZET")
print("=" * 70)
print(f"  Toplam node: {real_nodes}")
print(f"  Toplam segment: {real_segments}")
print(f"  Toplam equipment: {real_equipment}")
print(f"  Toplam telemetry entity: {real_telemetry_entities}")
print(f"  Toplam telemetry tag: {real_telemetry_tags}")
print(f"  Ölü varlik (telemetsiz): nodes={len(dead_nodes)}, segments={len(dead_segments)}, equipment={len(dead_equipment)}")
print(f"  ID uyuşmazligi: {mismatch_count}")
print(f"  entity_type tutarsizligi: {'segment vs pipeline_segment' if 'segment' in entity_type_counts and 'pipeline_segment' in entity_type_counts else 'YOK'}")
print(f"  Fiziksel hata: {physics_errors}")
