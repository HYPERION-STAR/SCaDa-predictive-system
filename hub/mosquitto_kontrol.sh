#!/bin/bash

# Kontrol edilecek sürecin (process) adı
PROCESS_NAME="mosquitto"

# pgrep komutu sürecin aktif olup olmadığını (PID numarasını) kontrol eder
if pgrep -x "$PROCESS_NAME" > /dev/null
then
    # Eğer süreç bulunursa her şey yolundadır
    echo "$(date '+%Y-%m-%d %H:%M:%S') - [OK] $PROCESS_NAME sorunsuz çalışıyor."
else
    # Eğer süreç bulunamazsa servis çökmüştür
    echo "$(date '+%Y-%m-%d %H:%M:%S') - [CRITICAL] $PROCESS_NAME durmuş! Yeniden başlatılıyor..."
    
    # Ubuntu mimarisine göre servisi yeniden başlatmayı dener
    sudo service mosquitto start || sudo systemctl start mosquitto

    # Yeniden başlatma işleminin sonucunu kontrol et
    sleep 2
    if pgrep -x "$PROCESS_NAME" > /dev/null
    then
        echo "$(date '+%Y-%m-%d %H:%M:%S') - [INFO] $PROCESS_NAME başarıyla kurtarıldı."
    else
        echo "$(date '+%Y-%m-%d %H:%M:%S') - [ERROR] $PROCESS_NAME başlatılamadı! Manuel müdahale gerekiyor."
    fi
fi

#Eğer bu denetimi otomatik bir döngüye bağlamak istersen şu komutla crontab tablosunu
#açabilirsin: crontab -e
#*/5 * * * * /home/kullanici_adin/staj_mqtt_projesi/mosquitto_kontrol.sh >> /home/kullanici_adin/staj_mqtt_projesi/mosquitto_log.txt 2>&1
