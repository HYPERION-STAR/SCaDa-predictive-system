"""
SCaDa Kestirimci Bakim - Tahmin API
====================================
Kisi 1: AI & Veri Bilimi - Hafta 3

Bu modul, egitilmis modelleri yukler ve gercek zamanli
RUL tahmini + anomali tespiti yapar.

Kullanim:
    from model_predictor import Predictor
    
    predictor = Predictor()
    result = predictor.predict({
        'sensor_1': 250.0, 'sensor_2': 1000.0, ...
    })
    
    print(result)
    # {'rul': 45.2, 'health_score': 34.8, 'anomaly': True}

Takim Entegrasyonu:
    - Kisi 2 (MQTT): factory/motor/sensor_data topic'inden veri alir
    - Kisi 3 (C# Dashboard): {rul, health_score, anomaly} formatinda veri gonderir
    - Kisi 4 (DB): TahminSonuclari tablosuna kaydeder
"""
import json
import numpy as np
import pandas as pd
import joblib
import os

# Kullanim sinirlamalarini sabitle
MAX_RUL = 130  # Veri setindeki maksimum dongu


class Predictor:
    """Kestirimci bakim modeli yukler ve tahmin yapar."""
    
    def __init__(self, model_dir='models'):
        """
        Modelleri yukle.
        
        Args:
            model_dir: Model dosyalarinin bulundugu klasör
        """
        self.model_dir = model_dir
        self._load_models()
        
    def _load_models(self):
        """Tum modelleri ve scaler'i yukle."""
        print("Modeller yukleniyor...")
        
        # Random Forest Regresyon (RUL Tahmini)
        self.rf_reg = joblib.load(os.path.join(self.model_dir, 'random_forest_rul.pkl'))
        print("  [OK] Random Forest RUL modeli")
        
        # Random Forest Anomali Tespiti
        self.rf_clf = joblib.load(os.path.join(self.model_dir, 'random_forest_anomaly.pkl'))
        print("  [OK] Random Forest Anomali modeli")
        
        # Scaler
        self.scaler = joblib.load(os.path.join(self.model_dir, 'scaler.pkl'))
        print("  [OK] StandardScaler")
        
        # Feature kolon isimleri
        self.sensor_cols = [f'sensor_{i}' for i in range(1, 22)]
        self.feature_cols = self._build_feature_cols()
        
        print(f"  Toplam feature: {len(self.feature_cols)}")
        print("Tum modeller hazir!\n")
    
    def _build_feature_cols(self):
        """Egitim sirasinda olusturulan feature kolonlarini olustur."""
        base_cols = ['time_in_cycles', 'op_setting_1', 'op_setting_2', 'op_setting_3']
        sensor_cols = self.sensor_cols
        rolling_cols = [f'{s}_rmean' for s in sensor_cols] + \
                       [f'{s}_rstd' for s in sensor_cols]
        diff_cols = [f'{s}_diff' for s in sensor_cols]
        
        return base_cols + sensor_cols + rolling_cols + diff_cols
    
    def _prepare_features(self, sensor_readings, history_buffer=None):
        """
        Ham sensör okumalarini feature vektörüne cevir.
        
        Args:
            sensor_readings: dict {'sensor_1': 45.2, 'sensor_2': 1013.5, ...}
            history_buffer: oncedeki okumalar (kaydirma penceresi icin)
            
        Returns:
            np.array: ölceklendirilmis feature vektörü
        """
        # DataFrame'e cevir
        df = pd.DataFrame([sensor_readings])
        
        # Eksik sensörleri default degerlerle doldur (ortalama degerler)
        default_sensors = {
            'sensor_1': 250.0, 'sensor_2': 1000.0, 'sensor_3': 1.0,
            'sensor_4': 1800.0, 'sensor_5': 21.0, 'sensor_6': 100.0,
            'sensor_7': 500.0, 'sensor_8': 2000.0, 'sensor_9': 150.0,
            'sensor_10': 20.0, 'sensor_11': 3000.0, 'sensor_12': 50.0,
            'sensor_13': 1000.0, 'sensor_14': 250.0, 'sensor_15': 2000.0,
            'sensor_16': 30.0, 'sensor_17': 150.0, 'sensor_18': 1800.0,
            'sensor_19': 250.0, 'sensor_20': 100.0, 'sensor_21': 0.05,
        }
        for sensor, default_val in default_sensors.items():
            if sensor not in df.columns:
                df[sensor] = default_val
        
        # Zaman bilgisi ekle (yoksa 0)
        if 'time_in_cycles' not in df.columns:
            df['time_in_cycles'] = 0
        if 'op_setting_1' not in df.columns:
            df['op_setting_1'] = 0.8
        if 'op_setting_2' not in df.columns:
            df['op_setting_2'] = 0.6
        if 'op_setting_3' not in df.columns:
            df['op_setting_3'] = 0.9
        
        # Kaydirma pencere ozellikleri ekle
        for sensor in self.sensor_cols:
            df[f'{sensor}_rmean'] = df[sensor]
            df[f'{sensor}_rstd'] = 0.0
            df[f'{sensor}_diff'] = 0.0
        
        # Gecmis buffer varsa rolling ozellikleri hesapla
        if history_buffer is not None and len(history_buffer) > 1:
            hist_df = pd.DataFrame(history_buffer)
            for sensor in self.sensor_cols:
                if sensor in hist_df.columns:
                    window = min(50, len(hist_df))
                    recent = hist_df[sensor].tail(window)
                    df[f'{sensor}_rmean'] = recent.mean()
                    df[f'{sensor}_rstd'] = recent.std() if len(recent) > 1 else 0.0
                    if len(hist_df) > 1:
                        df[f'{sensor}_diff'] = hist_df[sensor].iloc[-1] - hist_df[sensor].iloc[-2]
        
        # Feature vektörünü al (eksik kolonlari 0 ile doldur)
        for col in self.feature_cols:
            if col not in df.columns:
                df[col] = 0.0
        
        features = df[self.feature_cols].values
        
        # Ölceklendir
        X_scaled = self.scaler.transform(features)
        
        return X_scaled[0]
    
    def predict(self, sensor_readings, history_buffer=None):
        """
        Sensör okumalarini alir, RUL tahmini ve anomali bayragi döndürür.
        
        Args:
            sensor_readings: dict {'sensor_1': 45.2, 'sensor_2': 1013.5, ...}
            history_buffer: oncedeki okumalar listesi (kaydirma icin)
            
        Returns:
            dict: {
                'rul': float,           # Kalan有用 omur (dongu)
                'health_score': float,  # Saglik skoru (%0-100)
                'anomaly': bool,        # Anomali bayragi
                'is_degraded': bool     # Bozulmus mu?
            }
        """
        # Feature hazirla
        features = self._prepare_features(sensor_readings, history_buffer)
        
        # RUL Tahmini (Regresyon)
        rul_pred = float(self.rf_reg.predict(features.reshape(1, -1))[0])
        rul_pred = max(0.0, rul_pred)  # RUL negatif olamaz
        
        # Saglik skoru: RUL yuksekse %100, dusukse %0
        health_score = max(0.0, min(100.0, (rul_pred / MAX_RUL) * 100.0))
        
        # Anomali Tespiti (Siniflandirma)
        anomaly_prob = float(self.rf_clf.predict_proba(features.reshape(1, -1))[0][1])
        is_degraded = bool(anomaly_prob > 0.5)
        
        # Anomali: health_score < 20 veya degraded
        anomaly = health_score < 20 or is_degraded
        
        return {
            'rul': round(rul_pred, 2),
            'health_score': round(health_score, 2),
            'anomaly': anomaly,
            'is_degraded': is_degraded,
            'anomaly_probability': round(anomaly_prob, 4)
        }
    
    def predict_batch(self, sensor_data_list):
        """
        Toplu tahmin yapar.
        
        Args:
            sensor_data_list: list of dicts
            
        Returns:
            list of prediction dicts
        """
        results = []
        history = []
        
        for readings in sensor_data_list:
            result = self.predict(readings, history if history else None)
            results.append(result)
            history.append(readings)
            # Gecmis boyutunu sinirla
            if len(history) > 100:
                history = history[-50:]
        
        return results
    
    def get_feature_importance(self, top_n=15):
        """
        En onemli feature'lari döndür.
        
        Returns:
            list of (feature_name, importance) tuples
        """
        importances = self.rf_reg.feature_importances_
        feat_imp = sorted(
            zip(self.feature_cols, importances),
            key=lambda x: x[1],
            reverse=True
        )
        return feat_imp[:top_n]
    
    def model_info(self):
        """Model bilgilerini döndür."""
        return {
            'model_type': 'RandomForest',
            'regression_model': 'random_forest_rul.pkl',
            'classification_model': 'random_forest_anomaly.pkl',
            'n_features': len(self.feature_cols),
            'features': self.feature_cols,
            'max_rul': MAX_RUL,
            'sensor_count': len(self.sensor_cols)
        }


# =========================================================================
# Hızli Kullanim Ornegi
# =========================================================================
if __name__ == '__main__':
    print("=" * 60)
    print("SCaDa Kestirimci Bakim - Tahmin API Test")
    print("=" * 60)
    
    predictor = Predictor()
    
    # Ornek 1: Saglikli motor
    print("\n--- Senaryo 1: Saglikli Motor ---")
    healthy_readings = {
        'sensor_1': 250.0, 'sensor_2': 1000.0, 'sensor_3': 1.0,
        'sensor_4': 1800.0, 'sensor_5': 21.0, 'sensor_6': 100.0,
        'sensor_7': 500.0, 'sensor_8': 2000.0, 'sensor_9': 150.0,
        'sensor_10': 20.0, 'sensor_11': 3000.0, 'sensor_12': 50.0,
        'sensor_13': 1000.0, 'sensor_14': 250.0, 'sensor_15': 2000.0,
        'sensor_16': 30.0, 'sensor_17': 150.0, 'sensor_18': 1800.0,
        'sensor_19': 250.0, 'sensor_20': 100.0, 'sensor_21': 0.05,
    }
    result_healthy = predictor.predict(healthy_readings)
    print(json.dumps(result_healthy, indent=2))
    
    # Ornek 2: Bozulmus motor
    print("\n--- Senaryo 2: Bozulmus Motor ---")
    degraded_readings = healthy_readings.copy()
    # Sensör degerlerini boz (artan bozulma deseni)
    degraded_readings['sensor_21'] = 0.15   # 3x artti
    degraded_readings['sensor_3'] = 0.7     # Dustu
    degraded_readings['sensor_6'] = 150.0   # Artti
    degraded_readings['sensor_12'] = 80.0   # Artti
    degraded_readings['sensor_14'] = 180.0  # Dustu
    
    result_degraded = predictor.predict(degraded_readings)
    print(json.dumps(result_degraded, indent=2))
    
    # Ornek 3: Anomali (ani artis)
    print("\n--- Senaryo 3: Anomali (ani sensor artisi) ---")
    spike_readings = healthy_readings.copy()
    spike_readings['sensor_5'] = 63.0  # 3x normal deger
    
    result_spike = predictor.predict(spike_readings)
    print(json.dumps(result_spike, indent=2))
    
    # Feature onemliligi
    print("\n--- En Onemli 10 Feature ---")
    top_features = predictor.get_feature_importance(10)
    for i, (name, imp) in enumerate(top_features, 1):
        print(f"  {i:2d}. {name:30s} {imp:.4f}")
    
    # Model bilgisi
    print("\n--- Model Bilgisi ---")
    info = predictor.model_info()
    print(f"  Sensor sayisi: {info['sensor_count']}")
    print(f"  Feature sayisi: {info['n_features']}")
    print(f"  Max RUL: {info['max_rul']}")
    
    print("\n" + "=" * 60)
    print("Test tamamlandi!")
    print("=" * 60)
