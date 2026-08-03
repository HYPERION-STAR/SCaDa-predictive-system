"""
NASA C-MAPSS FD001 Sentetik Veri Üretici
Gerçek C-MAPSS verisine benzer yapıda turbofan motor sensör verisi üretir.
Her motor bozulana kadar calisir, RUL (Remaining Useful Life) hesaplar.
"""
import numpy as np
import pandas as pd
import os

np.random.seed(42)

NUM_ENGINES = 100
MAX_CYCLES = 200
NUM_SENSORS = 21
NUM_OP_SETTINGS = 3

# Sensörlerin baslangic degerleri ve bozulma oranlari
# format: (base_value, degradation_rate, noise_level)
sensor_profiles = {
    'sensor_1':  (250.0,  0.15, 3.0),
    'sensor_2':  (1000.0, 0.05, 5.0),
    'sensor_3':  (1.0,    0.002, 0.01),
    'sensor_4':  (1800.0, -0.5, 10.0),
    'sensor_5':  (21.0,   0.01, 0.3),
    'sensor_6':  (100.0,  0.2, 1.5),
    'sensor_7':  (500.0,  0.1, 5.0),
    'sensor_8':  (2000.0, -0.3, 8.0),
    'sensor_9':  (150.0,  0.08, 2.0),
    'sensor_10': (20.0,   0.005, 0.2),
    'sensor_11': (3000.0, -1.0, 15.0),
    'sensor_12': (50.0,   0.15, 1.0),
    'sensor_13': (1000.0, 0.2, 8.0),
    'sensor_14': (250.0,  -0.4, 5.0),
    'sensor_15': (2000.0, 0.5, 10.0),
    'sensor_16': (30.0,   0.02, 0.5),
    'sensor_17': (150.0,  0.1, 2.0),
    'sensor_18': (1800.0, -0.6, 12.0),
    'sensor_19': (250.0,  0.08, 3.0),
    'sensor_20': (100.0,  0.12, 2.0),
    'sensor_21': (0.05,   0.001, 0.005),
}

# Operasyonel ayarlar
op_setting_profiles = [
    (0.8, 0.05),   # op_setting_1: mean, std
    (0.6, 0.04),   # op_setting_2
    (0.9, 0.03),   # op_setting_3
]

output_dir = 'data/FD001'
os.makedirs(output_dir, exist_ok=True)

all_rows = []
rul_values = {}

for engine_id in range(1, NUM_ENGINES + 1):
    # Her motor icin farkli baslangic yipragi (uretim varyasyonu)
    birth_defect = np.random.uniform(0.7, 1.3)
    # Her motor icin farkli bozulma hiz (daha kisa veya daha uzun omur)
    life_factor = np.random.uniform(0.5, 1.5)
    true_life = int(MAX_CYCLES * life_factor)
    
    # Bozulma modeli: zamanla artan, sonlarda hizlanan
    for cycle in range(1, true_life + 1):
        # Bozulma miktarı: lineer + exponential (sonlarda hizlanan)
        degradation = birth_defect * (cycle / true_life) ** 1.5
        
        row = {
            'engine_id': engine_id,
            'time_in_cycles': cycle,
        }
        
        # Sensör degerleri
        for sensor_name, (base, deg_rate, noise) in sensor_profiles.items():
            value = base + deg_rate * cycle * birth_defect + deg_rate * degradation * 50
            value += np.random.normal(0, noise)
            row[sensor_name] = round(value, 4)
        
        # Operasyonel ayarlar
        for i, (mean, std) in enumerate(op_setting_profiles, 1):
            op_value = np.random.normal(mean, std)
            row[f'op_setting_{i}'] = round(max(0, op_value), 4)
        
        all_rows.append(row)
    
    rul_values[engine_id] = true_life

# DataFrame'e cevir
df = pd.DataFrame(all_rows)

# RUL hesapla (dusuk RUL = yakinda ariza)
df['RUL'] = df.apply(
    lambda r: rul_values[r['engine_id']] - r['time_in_cycles'], axis=1
)

# Train ve test setlerine ayır (ilk 80 motor train, son 20 test)
train_df = df[df['engine_id'] <= 80].copy()
test_df = df[df['engine_id'] > 80].copy()

# Kaggle formatinda kaydet (boşluklu, header yok)
col_names = ['engine_id', 'time_in_cycles',
             'op_setting_1', 'op_setting_2', 'op_setting_3']
for i in range(1, 22):
    col_names.append(f'sensor_{i}')

# Train set
train_df[col_names].to_csv(
    os.path.join(output_dir, 'train.txt'),
    sep=' ', index=False, header=False
)

# Test set
test_df[col_names].to_csv(
    os.path.join(output_dir, 'test.txt'),
    sep=' ', index=False, header=False
)

# RUL dosyasi (tum motorlar icin - son dongu RUL degeri)
# Kaggle FD001 formati: engine_id + RUL (son dongunun RUL'i)
all_rul = []
for eid in sorted(rul_values.keys()):
    # En son dongunun RUL degeri = 0 (ariza anina yakin)
    all_rul.append({'engine_id': eid, 'RUL': 0})

rul_df = pd.DataFrame(all_rul)
rul_df[['RUL']].to_csv(
    os.path.join(output_dir, 'RUL.txt'),
    sep=' ', index=False, header=False
)

print(f"Sentetik FD001 verisi üretildi!")
print(f"  Train: {len(train_df)} satir, {train_df['engine_id'].nunique()} motor")
print(f"  Test:  {len(test_df)} satir, {test_df['engine_id'].nunique()} motor")
print(f"  Ortalama true_life: {np.mean(list(rul_values.values())):.0f} dongu")
print(f"  Cikis: {output_dir}/")
