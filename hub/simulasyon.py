import time
import pandas as pd
import paho.mqtt.client as mqtt
import json

# 1. MQTT Broker Bağlantı Ayarları
BROKER_HOST = "localhost"
BROKER_PORT = 1883
TOPIC = "fabrika/motor/sensor_data"

# Paho-mqtt güncel sürüm uyumluluğu için client tanımı
client = mqtt.Client(callback_api_version=mqtt.CallbackAPIVersion.VERSION2)
client.connect(BROKER_HOST, BROKER_PORT, 60)

# 2. Veri Setini Yükleme Mantığı
try:
    #veri yolu
    data = pd.read_csv("train_FD001.txt", sep=r"\s+", header=None)
    print("Gerçek C-MAPSS veri seti yüklendi.")
except FileNotFoundError:
    print("Veri dosyası (train_FD001.txt) bulunamadı! Şimdilik test amaçlı sahte veri döngüsü çalışıyor...")
    # Dosya henüz yoksa C-MAPSS formatına benzer geçici bir yapı
    data = pd.DataFrame([{"motor_id": 1, "cycle": i, "sicaklik": 640 + (i*0.1), "titresim": 518} for i in range(1, 100)])

print("Simülasyon başlatılıyor. Durdurmak için Ctrl+C yapabilirsin.\n")

# 3. Verileri Satır Satır Okuyup MQTT'ye Gönderme
try:
    for index, row in data.iterrows():
        # Yapay zeka ve C# arayüzü rahat okusun diye veriyi JSON formatına çeviriyoruz
        payload = row.to_dict()
        json_payload = json.dumps(payload)
        
        # Mesajı broker'a fırlatıyoruz
        client.publish(TOPIC, json_payload)
        print(f"Yayınlanan Veri [Satır {index}]: {json_payload}")
        
        # Gerçek zamanlı akış hissi için 1 saniye bekle
        time.sleep(1)

except KeyboardInterrupt:
    print("\nSimülasyon durduruldu.")
finally:
    client.disconnect()
