"""
Gerçek NASA C-MAPSS FD001 verisini projeye hazirla
"""
import os
import pandas as pd
import numpy as np

raw_dir = 'data/CMAPSS_raw'
fd001_dir = 'data/FD001'
os.makedirs(fd001_dir, exist_ok=True)

print("=" * 60)
print("NASA C-MAPSS FD001 - Gerçek Veri Hazirlaniyor")
print("=" * 60)

# Gerçek veriyi yukle
# Format: engine_id, time_in_cycles, op_setting_1-3, sensor_1-21 (26 sütun)
col_names = ['engine_id', 'time_in_cycles',
             'op_setting_1', 'op_setting_2', 'op_setting_3']
for i in range(1, 22):  # 21 sensör
    col_names.append(f'sensor_{i}')

train = pd.read_csv(f'{raw_dir}/train_FD001.txt', sep=r'\s+', header=None)
train.columns = col_names

test = pd.read_csv(f'{raw_dir}/test_FD001.txt', sep=r'\s+', header=None)
test.columns = col_names

# RUL dosyasi - tek sütun, 100 satir (test engine'lar icin)
rul = pd.read_csv(f'{raw_dir}/RUL_FD001.txt', sep=r'\s+', header=None)

print(f"\nTrain: {train.shape}")
print(f"Test:  {test.shape}")
print(f"RUL:   {rul.shape}")

# Train set'e RUL ekle (her engine icin son dongu RUL = 0)
max_cycles = train.groupby('engine_id')['time_in_cycles'].max()
train['true_RUL'] = train.apply(
    lambda r: max_cycles[r['engine_id']] - r['time_in_cycles'], axis=1
)

# Test set'e RUL ekle (her engine icin RUL degeri tüm satirlara)
test['engine_id'] = test['engine_id'].astype(int)
rul_df = pd.DataFrame({'engine_id': range(1, 101), 'true_RUL': rul[0].values})
test_with_rul = test.merge(rul_df, on='engine_id', how='left')

# Dosyalari kaydet (bizim formatimizla uyumlu)
train[col_names].to_csv(f'{fd001_dir}/train.txt', sep=' ', index=False, header=False)
test[col_names].to_csv(f'{fd001_dir}/test.txt', sep=' ', index=False, header=False)

# RUL dosyasi - test set icin
rul.to_csv(f'{fd001_dir}/RUL.txt', sep=' ', index=False, header=False)

print(f"\nFD001 dosyalari kaydedildi:")
for f in ['train.txt', 'test.txt', 'RUL.txt']:
    path = f'{fd001_dir}/{f}'
    size = os.path.getsize(path)
    print(f"  {f} ({size/1024:.0f} KB)")

# Istatistikler
print(f"\n--- FD001 Istatistikleri ---")
print(f"  Train motorlar: {train['engine_id'].nunique()}")
print(f"  Test motorlar:  {test['engine_id'].nunique()}")
print(f"  Train satirlar: {len(train):,}")
print(f"  Test satirlar:  {len(test):,}")
print(f"  Sensör sayisi:  21")
print(f"  Ortalama train dongu: {max_cycles.mean():.0f}")
print(f"  Min/Max dongu: {max_cycles.min()} / {max_cycles.max()}")

# Test RUL istatistikleri
print(f"\nTest RUL istatistikleri:")
print(f"  Ortalama RUL: {rul[0].mean():.0f}")
print(f"  Min RUL:      {rul[0].min()}")
print(f"  Max RUL:      {rul[0].max()}")

print("\n" + "=" * 60)
print("Hazir! Simdi EDA ve model egitimi yeniden calistirabilirsiniz.")
print("  python eda_script.py")
print("  python model_training.py")
print("=" * 60)
