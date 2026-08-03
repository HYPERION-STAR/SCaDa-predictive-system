import json
import sys
sys.stdout.reconfigure(encoding='utf-8')

with open(r'C:\Users\HELIOS\Projects\SCaDa_predictive_maintenance\data\SCaDa_live_snapshot.json', 'r', encoding='utf-8') as f:
    data = json.load(f)

top_regions = [k for k in data.keys() if k != 'meta']

# ─── CS-ESKISEHIR-U2 cift kayit ─────────────────────────────────────────────
print("=== CS-ESKISEHIR-U2 CIFT KAYIT ===")
for rk in top_regions:
    region = data[rk]
    for eq in region.get('equipment', []):
        if eq['unit_id'] == 'CS-ESKISEHIR-U2':
            print(f"  {rk}: unit_id={eq['unit_id']} station={eq.get('station_node_id','')} model={eq.get('model','')}")

# ─── Global duplicate kontrolu ──────────────────────────────────────────────
print("\n=== GLOBAL DUPLICATE KONTROLU ===")
from collections import Counter

all_eq_uids = []
for rk in top_regions:
    for eq in data[rk].get('equipment', []):
        all_eq_uids.append((rk, eq['unit_id']))

eq_counts = Counter(uid for _, uid in all_eq_uids)
dupes = {k:v for k,v in eq_counts.items() if v > 1}
print(f"  Equipment duplicate unit_ids: {dupes}")

all_telem_eids = []
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        all_telem_eids.append((rk, telem.get('entity_id','')))

telem_counts = Counter(eid for _, eid in all_telem_eids)
telem_dupes = {k:v for k,v in telem_counts.items() if v > 1}
print(f"  Telemetry duplicate entity_ids: {telem_dupes}")

# ─── total_telemetry_tags dogrulama ─────────────────────────────────────────
print("\n=== TELEMETRY TAG SAYIMI ===")

# Yontem 1: telemetry kayit sayisi (varlik bazli)
total_records = sum(len(data[r].get('telemetry', {})) for r in top_regions)
print(f"  Telemetry records (varlik): {total_records}")

# Yontem 2: her entity_type icin tag sayimi
tag_counts = {}
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        et = telem.get('entity_type', '')
        if et == 'compressor':
            tag_counts['compressor'] = tag_counts.get('compressor', 0) + 25
        elif et == 'segment':
            tag_counts['segment'] = tag_counts.get('segment', 0) + 9
        elif et == 'border_entry':
            tag_counts['border_entry'] = tag_counts.get('border_entry', 0) + 5
        elif et in ('ugs','fsru','lng_terminal'):
            tag_counts[et] = tag_counts.get(et, 0) + 8
        else:
            tag_counts[et] = tag_counts.get(et, 0) + len(telem)

print(f"  Hesaplanan tag sayisi:")
for t, c in sorted(tag_counts.items()):
    print(f"    {t}: {c}")
total_hesaplanan = sum(tag_counts.values())
print(f"    TOPLAM: {total_hesaplanan}")

# Yontem 3: gercek telemetry sutun sayisi (her kayit icin key sayisi)
gercek_sutun = 0
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        gercek_sutun += len(telem)
print(f"\n  Gercek telemetry sutun (her key bir tag): {gercek_sutun}")

# Yontem 4: sensor tag sayimi (s_1..s_21 + op_1..op_4)
sensor_tags = 0
for rk in top_regions:
    for tid, telem in data[rk].get('telemetry', {}).items():
        et = telem.get('entity_type', '')
        if et == 'compressor':
            # s_1..s_21 = 21, op_1..op_4 = 4, entity_id, entity_type, quality, status = 4
            sensor_tags += 29
        elif et == 'segment':
            # s_inlet_pressure_bar, s_outlet_pressure_bar, s_dp_bar, s_flow_m3_h,
            # s_outflow_m3_h, s_mass_imbalance_pct, s_temperature_c, s_cathodic_protection_v, quality = 9
            sensor_tags += 9
        elif et == 'border_entry':
            # flow_m3_h, pressure_bar, temperature_c, calorific_value_mj_m3, quality = 5
            sensor_tags += 5
        elif et == 'ugs':
            # s_storage_level_pct, s_inventory_mcm, s_injection_flow_m3_h,
            # s_withdrawal_flow_m3_h, s_pressure_bar, s_cavern_temp_c,
            # s_daily_capacity_mcm_day, s_working_capacity_pct, quality = 9
            sensor_tags += 9
        elif et == 'fsru':
            # s_storage_level_pct, s_pressure_bar, s_boil_off_rate_pct,
            # s_liquid_flow_m3_h, s_vapor_flow_m3_h, s_tank_temp_c, quality = 7
            sensor_tags += 7
        elif et == 'lng_terminal':
            # s_storage_level_pct, s_pressure_bar, s_boil_off_rate_pct,
            # s_regas_flow_m3_h, s_tank_temp_c, quality = 6
            sensor_tags += 6

print(f"  Sensor tag sayimi (entity_id, entity_type, quality dahil): {sensor_tags}")

print(f"\n  meta.total_telemetry_tags = {data['meta']['total_telemetry_tags']}")
print(f"  Hangi sayimla eslesiyor?")
print(f"    records={total_records}: {'EVET' if data['meta']['total_telemetry_tags']==total_records else 'HAYIR'}")
print(f"    hesaplanan={total_hesaplanan}: {'EVET' if data['meta']['total_telemetry_tags']==total_hesaplanan else 'HAYIR'}")
print(f"    gercek_sutun={gercek_sutun}: {'EVET' if data['meta']['total_telemetry_tags']==gercek_sutun else 'HAYIR'}")
print(f"    sensor_tags={sensor_tags}: {'EVET' if data['meta']['total_telemetry_tags']==sensor_tags else 'HAYIR'}")
