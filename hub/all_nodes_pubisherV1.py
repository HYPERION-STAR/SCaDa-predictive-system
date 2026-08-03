import json
import time
import threading
import paho.mqtt.client as mqtt

BROKER_HOST = "127.0.0.1"
BROKER_PORT = 1883
MASTER_TOPIC = "SCaDa/MASTER/telemetry"

master_cache = {}

def on_connect(client, userdata, flags, rc, properties=None):
    print("[MQTT] Broker bağlandı. SCaDa/# dinleniyor...")
    client.subscribe("SCaDa/#")

def on_message(client, userdata, msg):
    # Kendi yayınladığı mesajı yoksay
    if "MASTER" in msg.topic:
        return
        
    try:
        payload_str = msg.payload.decode()
        data = json.loads(payload_str)
        
        entity_id = data.get("entity_id") or data.get("node_id")
        if entity_id:
            master_cache[entity_id] = data
            
    except Exception as e:
        print(f"[ERROR] Hata: {e}")

def master_publish_loop(client):
    while True:
        time.sleep(5)
        if master_cache:
            master_payload = {
                "timestamp": time.time(),
                "total_nodes": len(master_cache),
                "nodes": master_cache
            }
            client.publish(MASTER_TOPIC, json.dumps(master_payload))
            print(f"[MASTER PUBLISH] {len(master_cache)} cihaz verisi iletildi -> {MASTER_TOPIC}")

client = mqtt.Client(callback_api_version=mqtt.CallbackAPIVersion.VERSION2)
client.on_connect = on_connect
client.on_message = on_message

client.connect(BROKER_HOST, BROKER_PORT)

yayin_thread = threading.Thread(target=master_publish_loop, args=(client,), daemon=True)
yayin_thread.start()

client.loop_forever()