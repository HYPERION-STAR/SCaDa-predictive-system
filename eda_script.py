"""
Hafta 1: EDA - Jupyter olmadan calistirilabilir versiyon
"""
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import pandas as pd
import numpy as np
import matplotlib
matplotlib.use('Agg')  # Non-interactive backend
import matplotlib.pyplot as plt
import seaborn as sns
import os

plt.style.use('seaborn-v0_8-whitegrid')
output_dir = 'figures'
os.makedirs(output_dir, exist_ok=True)

print("=" * 60)
print("SCaDa Kestirimci Bakim - EDA Basliyor")
print("=" * 60)

# 1. Veri Yukleme
print("\n[1/9] Veri yukleniyor...")
first_line = open('data/FD001/train.txt').readline().split()
num_cols = len(first_line)
col_names = ['engine_id', 'time_in_cycles',
             'op_setting_1', 'op_setting_2', 'op_setting_3']
for i in range(1, num_cols - 5 + 1):
    col_names.append(f'sensor_{i}')

train = pd.read_csv('data/FD001/train.txt', sep=r'\s+', header=None)
train.columns = col_names

# RUL'u veriden hesapla (her motor icin max cycle - current cycle)
max_cycles = train.groupby('engine_id')['time_in_cycles'].max().to_dict()
train['RUL'] = train.apply(lambda r: max_cycles[r['engine_id']] - r['time_in_cycles'], axis=1)

sensor_cols = [col for col in train.columns if col.startswith('sensor_')]
print(f"  Veri seti: {train.shape}")
print(f"  Motor sayisi: {train['engine_id'].nunique()}")
print(f"  Sensör sayisi: {len(sensor_cols)}")

# 2. Temel Istatistikler
print("\n[2/9] Temel istatistikler hesaplanıyor...")
print(f"  Eksik deger: {train.isnull().sum().sum()}")
print(f"  RUL ort: {train['RUL'].mean():.1f}, medyan: {train['RUL'].median():.0f}")

# 3. RUL Dagilimi
print("\n[3/9] RUL dagilim grafigi olusturuluyor...")
plt.figure(figsize=(14, 6))
plt.hist(train['RUL'], bins=100, edgecolor='black', color='steelblue')
plt.title('RUL Dagilimi (Tum Motorlar)')
plt.xlabel('Kalan Omur (dongu)')
plt.ylabel('Motor Sayisi')
plt.axvline(train['RUL'].median(), color='red', linestyle='--',
            label=f"Medyan: {train['RUL'].median():.0f}")
plt.legend()
plt.tight_layout()
plt.savefig(f'{output_dir}/rul_distribution.png', dpi=150)
plt.close()
print("  -> figures/rul_distribution.png")

# 4. Sensör Trendleri
print("\n[4/9] Sensör trendleri analiz ediliyor...")
engine_id = train['engine_id'].iloc[0]
engine_data = train[train['engine_id'] == engine_id].sort_values('time_in_cycles')
print(f"  Ornek motor: #{engine_id}, {len(engine_data)} dongu")

num_sensors = len(sensor_cols)
n_rows = (num_sensors + 2) // 3
plt.figure(figsize=(16, 4 * n_rows))
for idx, sensor in enumerate(sensor_cols, 1):
    plt.subplot(n_rows, 3, idx)
    plt.plot(engine_data['time_in_cycles'], engine_data[sensor],
             linewidth=0.8, color='steelblue')
    plt.title(f'{sensor}', fontsize=9)
    plt.grid(True, alpha=0.3)
plt.suptitle(f'Motor #{engine_id} - Sensör Trendleri', fontsize=14, y=1.01)
plt.tight_layout()
plt.savefig(f'{output_dir}/sensor_trends.png', dpi=150, bbox_inches='tight')
plt.close()
print("  -> figures/sensor_trends.png")

# 5. Bozulma Desenleri
print("\n[5/9] Bozulma desenleri hesaplanıyor...")
early = engine_data[sensor_cols].head(50).mean()
late = engine_data[sensor_cols].tail(50).mean()
diff = late - early

plt.figure(figsize=(14, 6))
colors = ['red' if v < 0 else 'green' for v in diff.values]
plt.bar(range(num_sensors), diff.values, edgecolor='black', color=colors)
plt.xticks(range(num_sensors), [f'S{i+1}' for i in range(num_sensors)], rotation=45)
plt.title('Sensör Bozulmasi: Baslangic vs Son')
plt.xlabel('Sensör Index')
plt.ylabel('Ortalama Degisim')
plt.axhline(y=0, color='black', linewidth=0.5)
plt.tight_layout()
plt.savefig(f'{output_dir}/sensor_degradation.png', dpi=150)
plt.close()
print("  -> figures/sensor_degradation.png")

# En cok degisen sensörler
diff_sorted = diff.sort_values(ascending=False)
print("\n  En cok degisen 5 sensör:")
for i, (sensor, val) in enumerate(diff_sorted.head(5).items()):
    print(f"    {sensor}: {val:+.4f}")

# 6. Ozellik Muhendisligi
print("\n[6/9] Ozellik muhendisligi yapiliyor...")

def add_rolling_features(df, window=50):
    df = df.copy()
    sensor_cols = [col for col in df.columns if col.startswith('sensor_')]
    for engine_id_g, group in df.groupby('engine_id'):
        for sensor in sensor_cols:
            rolling_mean = group[sensor].rolling(window=window, min_periods=1).mean()
            rolling_std = group[sensor].rolling(window=window, min_periods=1).std()
            df.loc[group.index, f'{sensor}_rmean'] = rolling_mean
            df.loc[group.index, f'{sensor}_rstd'] = rolling_std
    return df

def add_difference_features(df):
    """Sadece ham sensörler icin fark hesapla (rolling ozellikleri icin degil)."""
    df = df.copy()
    sensor_cols = [col for col in df.columns if col.startswith('sensor_') and '_r' not in col]
    for engine_id_g, group in df.groupby('engine_id'):
        for sensor in sensor_cols:
            df.loc[group.index, f'{sensor}_diff'] = group[sensor].diff().fillna(0)
    return df

train_eng = add_rolling_features(train, window=50)
train_eng = add_difference_features(train_eng)
print(f"  Toplam ozellik: {len(train_eng.columns)}")

# 7. RUL ile Korelasyon
print("\n[7/9] RUL korelasyon analizi...")
correlations = []
for sensor in sensor_cols:
    corr = train_eng[sensor].corr(train_eng['RUL'])
    correlations.append((sensor, corr))
correlations.sort(key=lambda x: abs(x[1]), reverse=True)

print("  RUL ile en güçlü korelasyonlar:")
for name, corr in correlations[:5]:
    print(f"    {name}: {corr:.4f}")

# 8. Feature set hazirla
print("\n[8/9] Feature set hazirlaniyor...")
feature_cols = ['time_in_cycles', 'op_setting_1', 'op_setting_2', 'op_setting_3'] + \
               sensor_cols + \
               [c for c in train_eng.columns if 'rmean' in c or 'rstd' in c] + \
               [c for c in train_eng.columns if '_diff' in c]
print(f"  Toplam feature: {len(feature_cols)}")

# 9. Kaydet
print("\n[9/9] Veri kaydediliyor...")
train_eng.to_csv('data/train_engineered.csv', index=False)
print(f"  -> data/train_engineered.csv ({train_eng.shape[0]:,} satir, {train_eng.shape[1]} kolon)")

print("\n" + "=" * 60)
print("EDA TAMAMLANDI!")
print("=" * 60)
print(f"\nCikis dosyalari:")
print(f"  - figures/rul_distribution.png")
print(f"  - figures/sensor_trends.png")
print(f"  - figures/sensor_degradation.png")
print(f"  - data/train_engineered.csv")
print(f"\nSonraki adim: model_training.py (Hafta 2)")
