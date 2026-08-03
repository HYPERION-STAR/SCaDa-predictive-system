import json
import paho.mqtt.client as mqtt
from contextlib import asynccontextmanager
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
from typing import List, Dict, Any

class SensorData(BaseModel):
    sensor_name: str
    value: float
    unit: str

class NodeStatus(BaseModel):
    node_id: str
    name: str = None
    region: str = None
    lat: float = None
    lon: float = None
    health_state: str
    telemetry: List[SensorData] = []
    live_equipment: List[Any] = None

latest_telemetry_cache = {}
mock_nodes: Dict[str, dict] = {} 
mock_segments: List[dict] = []

def load_topology():
    with open("SCaDa_initialized_snapshot.json", "r", encoding="utf-8") as f:
        buyuk_veri = json.load(f)
        
    for bolge_adi, bolge_verisi in buyuk_veri.items():
        if bolge_adi == "meta":
            continue
            
        for node in bolge_verisi.get("nodes", []):
            node_id_upper = node["node_id"].upper()
            mock_nodes[node_id_upper] = {
                "node_id": node_id_upper,
                "name": node.get("name"),
                "region": bolge_adi,
                "lat": node.get("lat"),
                "lon": node.get("lon"),
                "health_state": "Healthy",
                "telemetry": []
            }
            
        for segment in bolge_verisi.get("segments", []):
            segment["region"] = bolge_adi
            mock_segments.append(segment)
            
    print(f"[API] Topoloji yüklendi. Düğüm: {len(mock_nodes)}, Segment: {len(mock_segments)}")

def on_connect(client, userdata, flags, rc, properties=None): #!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
    print("[MQTT] Broker bağlandı.")
    client.subscribe("SCaDa/#")

def on_message(client, userdata, msg):
    try:
        payload_str = msg.payload.decode()
        telemetry_data = json.loads(payload_str)
        
        entity_id = telemetry_data.get("entity_id") or telemetry_data.get("node_id")
        
        if entity_id:
            latest_telemetry_cache[entity_id] = telemetry_data
            print(f"[MQTT RECEIVE] Cihaz: {entity_id} | Veri Cache'e yazıldı.")
            
    except Exception as e:
        print(f"[MQTT ERROR] Hata: {e}")

@asynccontextmanager
async def lifespan(app: FastAPI):
    load_topology() 
    
    client = mqtt.Client(callback_api_version=mqtt.CallbackAPIVersion.VERSION2)
    client.on_connect = on_connect 
    client.on_message = on_message
    
    client.connect("127.0.0.1", 1883)
    client.loop_start()
    
    yield
    
    client.loop_stop()
    client.disconnect()

app = FastAPI(title="SCaDaHub API Gateway", lifespan=lifespan)

@app.get("/")
def root():
    return {"message": "API çalışıyor."}

@app.get("/api/SCaDa/summary")
def get_summary():
    return {
        "message": "Topoloji yüklendi.",
        "total_nodes": len(mock_nodes),
        "total_segments": len(mock_segments)
    }

@app.get("/api/SCaDa/nodes", response_model=List[NodeStatus])
def get_nodes():
    return list(mock_nodes.values())

@app.get("/api/SCaDa/segments")
def get_all_segments():
    return mock_segments

@app.get("/api/SCaDa/live_data")
def get_live_data():
    return latest_telemetry_cache

@app.get("/api/SCaDa/node/{node_id}")
def get_node_aggregator(node_id: str):
    node_id_upper = node_id.upper()
    if node_id_upper not in mock_nodes:
        raise HTTPException(status_code=404, detail="Düğüm bulunamadı.")
    
    node_data = mock_nodes[node_id_upper].copy()
    node_data["live_equipment"] = []
    
    arama_anahtari = node_id_upper.replace("-01", "").replace("-02", "").replace("-03", "")
    
    for cache_key, cache_data in latest_telemetry_cache.items():
        if arama_anahtari in cache_key.upper():
            node_data["live_equipment"].append(cache_data)
            
    return node_data

@app.get("/api/SCaDa/region/{region_name}")
def get_region_data(region_name: str):
    bolge_nodes = [n for n in mock_nodes.values() if n["region"].lower() == region_name.lower()]
    bolge_segments = [s for s in mock_segments if s.get("region", "").lower() == region_name.lower()]
    
    if not bolge_nodes and not bolge_segments:
        raise HTTPException(status_code=404, detail="Bölge bulunamadı.")
        
    return {
        "region": region_name,
        "nodes": bolge_nodes,
        "segments": bolge_segments
    }

@app.get("/api/SCaDa/connected/{node_id}")
def get_connected_links(node_id: str):
    node_id_upper = node_id.upper()
    bagli_segmentler = []
    
    for s in mock_segments:
        if s.get("from_node") == node_id_upper or s.get("to_node") == node_id_upper:
            bagli_segmentler.append(s)
            
    return {
        "node_id": node_id_upper,
        "connected_segments": bagli_segmentler
    }