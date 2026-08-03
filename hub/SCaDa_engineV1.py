import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# ^ repo kökünü path'e ekler; degradation.py kökte, hub/ dışında
import json
import time
import copy
import random
import paho.mqtt.client as mqtt
from datetime import datetime

# Kişi 1'in modülü entegre ediliyor[cite: 9, 11]
from degradation import apply_degradation

BROKER_HOST = "127.0.0.1"
BROKER_PORT = 1883

def initialize_SCaDa_snapshot(input_file, output_file):
    with open(input_file, 'r', encoding='utf-8') as f:
        data = json.load(f)

    # Hata 2 Çözümü: Simülasyon çevrimi 0'dan başlar[cite: 9]
    if "meta" in data:
        data["meta"]["cycle_duration_hours"] = 1
        data["meta"]["simulation_cycle"] = 0

    referans_tarih = datetime(2026, 7, 20)
    ariza_tipleri = ["bearing_wear", "fouling", "seal_leak", "surge"]

    for bolge_adi, bolge_verisi in data.items():
        if bolge_adi == "meta": continue

        # OFFTAKE düğümlerine talep profili ekle[cite: 9]
        for node in bolge_verisi.get("nodes", []):
            if node.get("node_type") == "OFFTAKE":
                if "demand_profile" not in node:
                    node["demand_profile"] = {
                        "daily_curve": [0.6, 0.55, 0.5, 0.45, 0.48, 0.55, 0.7, 0.85, 0.95, 1.0, 0.98, 0.95,
                                        0.9, 0.88, 0.9, 0.95, 1.0, 1.05, 1.0, 0.9, 0.8, 0.7, 0.65, 0.6],
                        "seasonal_factor": 1.0
                    }

        # Kompresörlere 4 kalıcı parametre ekleme (Hata 2 ve Madde 2)[cite: 9]
        for equip in bolge_verisi.get("equipment", []):
            if equip.get("unit_type") == "compressor":
                install_date_str = equip.get("install_date", "2015-05-20")
                install_date = datetime.strptime(install_date_str, "%Y-%m-%d")
                fark_gun = (referans_tarih - install_date).days
                age_cycles = fark_gun * 24  # Başlangıç yaşı
                
                if "birth_defect" not in equip:
                    equip["birth_defect"] = round(random.uniform(0.7, 1.3), 3)
                    equip["life_factor"] = round(random.uniform(0.5, 1.5), 3)
                    equip["age_cycles"] = age_cycles  # Çifte sayım önlendi[cite: 9]
                    equip["expected_life_cycles"] = 175200
                    
                    # Test için örnek arıza programı (fault_schedule) ataması
                    if random.random() < 0.25:
                        equip["active_fault"] = random.choice(ariza_tipleri)
                        equip["fault_start_cycle"] = 100  # Simülasyonun 100. saatinde arıza başlar !!!!!!!!!!
                    else:
                        equip["active_fault"] = None
                        equip["fault_start_cycle"] = 0

        # Tip düzeltmeleri[cite: 9]
        for tel_key, tel_val in bolge_verisi.get("telemetry", {}).items():
            entity_id = tel_val.get("entity_id", "")
            if entity_id.startswith("OFFTAKE-"):
                tel_val["entity_type"] = "offtake"
            elif entity_id.startswith("JUNCTION-"):
                tel_val["entity_type"] = "junction"

    with open(output_file, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
        
    print(f"[INIT] {output_file} hazırlandı. Sabit taban ve 4 kalıcı parametre kilitlendi.")
    return data

def start_SCaDa_publisher():
    base_data = initialize_SCaDa_snapshot("SCaDa_live_snapshot.json", "SCaDa_base_snapshot.json")
    
    client = mqtt.Client(callback_api_version=mqtt.CallbackAPIVersion.VERSION2)
    client.connect(BROKER_HOST, BROKER_PORT)
    
    current_cycle = base_data.get("meta", {}).get("simulation_cycle", 0)
    print(f"[PUBLISHER] SCaDa yayını başlatıldı. Başlangıç Çevrimi: {current_cycle}")

    # Hata 3 Çözümü: Her ünite için değişmez "sağlıklı taban" (baseline) hafızası oluştur
    healthy_baselines = {}
    for bolge_adi, bolge_verisi in base_data.items():
        if bolge_adi == "meta": continue
        for cihaz_key, tel_val in bolge_verisi.get("telemetry", {}).items():
            entity_id = tel_val.get("entity_id")
            if entity_id:
                # Sadece kompresör/sensör verilerinin ham sağlıklı kopyasını sakla
                healthy_baselines[entity_id] = copy.deepcopy(tel_val)

    while True:
        try:
            current_cycle += 1
            anlik_zaman = datetime.now().isoformat()
            günün_saati = current_cycle % 24

            for bolge_adi, bolge_verisi in base_data.items():
                if bolge_adi == "meta": continue
                
                equipments = {eq["unit_id"]: eq for eq in bolge_verisi.get("equipment", [])}
                nodes_map = {n["node_id"]: n for n in bolge_verisi.get("nodes", [])}
                
                for cihaz_key, tel_val in bolge_verisi.get("telemetry", {}).items():
                    entity_id = tel_val.get("entity_id")
                    entity_type = tel_val.get("entity_type")
                    
                    tel_val["cycle"] = current_cycle
                    tel_val["timestamp"] = anlik_zaman

                    # Çalışma noktası varyasyonu (Demand profile)[cite: 9]
                    if entity_type == "offtake" and entity_id in nodes_map:
                        node_info = nodes_map[entity_id]
                        if "demand_profile" in node_info:
                            curve = node_info["demand_profile"]["daily_curve"]
                            multiplier = curve[günün_saati]
                            base_cap = node_info.get("capacity_mcm_day", 1000)
                            if "op_3_flow_demand_m3_h" in tel_val:
                                tel_val["op_3_flow_demand_m3_h"] = round(base_cap * multiplier * 100, 2)
                            if "op_4_speed_setpoint_pct" in tel_val:
                                tel_val["op_4_speed_setpoint_pct"] = round(multiplier * 100, 2)

                    # Bozulma ve Etiket Yönetimi (Hata 1, Hata 3 ve degradation.py Entegrasyonu)[cite: 9]
                    if entity_type == "compressor" and entity_id in equipments:
                        eq_params = equipments[entity_id]
                        
                        unit_params = {
                            **eq_params,
                            "active_fault": eq_params.get("active_fault"),
                            "fault_start_cycle": eq_params.get("fault_start_cycle", 0)
                        }
                        
                        original_healthy_frame = healthy_baselines.get(entity_id, copy.deepcopy(tel_val))
                        healthy_frame = copy.deepcopy(original_healthy_frame)
                        
                        # degradation.py fonksiyon çağrısı[cite: 9, 11]
                        degraded_frame, labels = apply_degradation(entity_id, healthy_frame, current_cycle, unit_params)
                        
                        tel_val.update(degraded_frame)
                        
                        # Dataclass nesne özelliklerini doğrudan alıyoruz[cite: 11] (TAHMIN KISMI ORN)
                        # tel_val["health_state"] = labels.health_state
                        # tel_val["fault_mode"] = labels.fault_mode
                        # tel_val["rul_cycles"] = labels.rul_cycles
                        # tel_val["health_score"] = labels.health_score

                    topic = f"SCaDa/{bolge_adi}/{entity_id}/telemetry"
                    client.publish(topic, json.dumps(tel_val))

            print(f"[PUBLISH] Cycle: {current_cycle} | Zaman: {anlik_zaman} | Başarıyla iletildi.")
            time.sleep(5)
            
        except KeyboardInterrupt:
            client.disconnect()
            print("\n[PUBLISHER] Yayın durduruldu.")
            break

if __name__ == "__main__":
    start_SCaDa_publisher()