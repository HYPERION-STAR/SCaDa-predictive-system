"""
Hafta 2: Model Egitimi
- Random Forest Regresyon (RUL Tahmini)
- Random Forest Siniflandirma (Anomali Tespiti)
- Model kayit (.pkl, .onnx)
- Ozellik onemliligi analizi
"""
import sys
import io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

import numpy as np
import pandas as pd
import os
import joblib
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from sklearn.preprocessing import StandardScaler
from sklearn.ensemble import RandomForestRegressor, RandomForestClassifier
from sklearn.model_selection import train_test_split
from sklearn.metrics import mean_absolute_error, r2_score, classification_report, confusion_matrix

print("=" * 60)
print("SCaDa Kestirimci Bakim - Model Egitimi")
print("=" * 60)

# =========================================================================
# 1. Veri Yukleme
# =========================================================================
print("\n[1/7] Veri yukleniyor...")
train_eng = pd.read_csv('data/train_engineered.csv')
print(f"  Sekil: {train_eng.shape}")
print(f"  Kolonlar: {len(train_eng.columns)}")

# Feature set
# Ham sensörler (rolling/diff icermeyenler)
sensor_cols = [col for col in train_eng.columns if col.startswith('sensor_') and '_r' not in col and '_diff' not in col]
rolling_cols = [c for c in train_eng.columns if c.endswith('_rmean') or c.endswith('_rstd')]
diff_cols = [c for c in train_eng.columns if c.endswith('_diff')]

feature_cols = ['time_in_cycles', 'op_setting_1', 'op_setting_2', 'op_setting_3'] + \
               sensor_cols + rolling_cols + diff_cols

print(f"  Feature sayisi: {len(feature_cols)}")

# =========================================================================
# 2. Veri Hazirlama
# =========================================================================
print("\n[2/7] Veri hazirlaniyor...")
X = train_eng[feature_cols].values
y_rul = train_eng['RUL'].values

# Ozellikleri ölceklendir
scaler = StandardScaler()
X_scaled = scaler.fit_transform(X)

print(f"  X sekli: {X_scaled.shape}")
print(f"  y_RUL ort: {y_rul.mean():.1f}, std: {y_rul.std():.1f}")

# Train/test split (motor bazli - veri sizmasini önlemek icin)
# Motor ID'lerine göre ayir (ilk 64 motor train, son 16 test)
engine_ids = train_eng['engine_id'].unique()
train_eng_ids = sorted(engine_ids[:int(len(engine_ids) * 0.8)])
test_eng_ids = sorted(engine_ids[int(len(engine_ids) * 0.8):])

train_mask = train_eng['engine_id'].isin(train_eng_ids)
test_mask = train_eng['engine_id'].isin(test_eng_ids)

X_train = X_scaled[train_mask]
X_test = X_scaled[test_mask]
y_train = y_rul[train_mask]
y_test = y_rul[test_mask]

print(f"  Train: {X_train.shape[0]} örnek, {len(train_eng_ids)} motor")
print(f"  Test:  {X_test.shape[0]} örnek, {len(test_eng_ids)} motor")

# =========================================================================
# 3. REGRESYON MODEL - Random Forest (RUL Tahmini)
# =========================================================================
print("\n[3/7] Random Forest Regresyon modeli egitiliyor...")

rf_reg = RandomForestRegressor(
    n_estimators=200,
    max_depth=25,
    min_samples_split=5,
    min_samples_leaf=2,
    random_state=42,
    n_jobs=-1
)
rf_reg.fit(X_train, y_train)

# Degerlendirme
y_pred_reg = rf_reg.predict(X_test)
mae_reg = mean_absolute_error(y_test, y_pred_reg)
r2_reg = r2_score(y_test, y_pred_reg)

print(f"\n  Regresyon Performansi (Test):")
print(f"    MAE:  {mae_reg:.2f} dongu")
print(f"    R²:   {r2_reg:.4f}")

# Ozellik onemliligi
importances = rf_reg.feature_importances_
feat_imp = pd.Series(importances, index=feature_cols).sort_values(ascending=False)

print(f"\n  En onemli 15 feature:")
for i, (feat, imp) in enumerate(feat_imp.head(15).items(), 1):
    print(f"    {i:2d}. {feat:30s} {imp:.4f}")

# Feature importance grafigi
plt.figure(figsize=(12, 8))
feat_imp.head(15).plot(kind='barh', color='steelblue')
plt.title('En Onemli 15 Feature (Random Forest Regresyon)', fontsize=13)
plt.xlabel('Onem Puanı')
plt.tight_layout()
plt.savefig('figures/feature_importance.png', dpi=150)
plt.close()
print("  -> figures/feature_importance.png")

# Gerçek vs Tahmin grafigi
plt.figure(figsize=(10, 8))
plt.scatter(y_test, y_pred_reg, alpha=0.3, s=15, color='steelblue')
plt.plot([y_test.min(), y_test.max()], [y_test.min(), y_test.max()], 'r--', lw=2)
plt.xlabel('Gercek RUL', fontsize=12)
plt.ylabel('Tahmin Edilen RUL', fontsize=12)
plt.title(f'Gerçek vs Tahmin Edilen RUL (MAE={mae_reg:.1f})', fontsize=13)
plt.grid(True, alpha=0.3)
plt.tight_layout()
plt.savefig('figures/rul_prediction_scatter.png', dpi=150)
plt.close()
print("  -> figures/rul_prediction_scatter.png")

# =========================================================================
# 4. SINIFLANDIRMA MODEL - Anomali Tespiti
# =========================================================================
print("\n[4/7] Anomali Tespiti modeli egitiliyor...")

threshold = 30  # RUL < 30 = "degraded" (arizaya yakin)
y_class = (train_eng['RUL'] < threshold).astype(int)

X_class = X_scaled
X_train_cl, X_test_cl, y_train_cl, y_test_cl = train_test_split(
    X_class, y_class, test_size=0.2, random_state=42, stratify=y_class
)

rf_clf = RandomForestClassifier(
    n_estimators=200,
    max_depth=25,
    random_state=42,
    n_jobs=-1,
    class_weight='balanced'  # Dengesiz siniflar icin
)
rf_clf.fit(X_train_cl, y_train_cl)

y_pred_cl = rf_clf.predict(X_test_cl)
y_prob_cl = rf_clf.predict_proba(X_test_cl)[:, 1]

print(f"\n  Siniflandirma Performansi (Test):")
print(f"    Degisken (normal/degraded): {y_class.mean()*100:.1f}% degraded")
print(f"\n  Classification Report:")
print(classification_report(y_test_cl, y_pred_cl, target_names=['Normal', 'Degraded']))

# Confusion matrix
cm = confusion_matrix(y_test_cl, y_pred_cl)
plt.figure(figsize=(8, 6))
plt.imshow(cm, interpolation='nearest', cmap=plt.cm.Blues)
plt.title('Confusion Matrix - Anomali Tespiti', fontsize=13)
plt.colorbar()
tick_marks = [0, 1]
plt.xticks(tick_marks, ['Normal', 'Degraded'])
plt.yticks(tick_marks, ['Normal', 'Degraded'])

for i in range(2):
    for j in range(2):
        plt.text(j, i, str(cm[i, j]), ha='center', va='center',
                 color='white', fontsize=16)

plt.ylabel('Gerçek')
plt.xlabel('Tahmin')
plt.tight_layout()
plt.savefig('figures/confusion_matrix.png', dpi=150)
plt.close()
print("  -> figures/confusion_matrix.png")

# =========================================================================
# 5. LSTM MODEL (Secenek - GPU varsa)
# =========================================================================
print("\n[5/7] LSTM modeli hazirlaniyor...")

try:
    from tensorflow.keras.models import Sequential
    from tensorflow.keras.layers import LSTM, Dense, Dropout
    from tensorflow.keras.callbacks import EarlyStopping

    timesteps = 50
    X_lstm = []
    y_lstm = []

    for engine_id, group in train_eng.groupby('engine_id'):
        if engine_id not in train_eng_ids:
            continue  # Sadece train motorlari kullan
        group = group.sort_values('time_in_cycles')
        values = group[feature_cols].values
        rul_values = group['RUL'].values
        for i in range(timesteps, len(group)):
            X_lstm.append(values[i-timesteps:i])
            y_lstm.append(rul_values[i])

    if len(X_lstm) > 0:
        X_lstm = np.array(X_lstm)
        y_lstm = np.array(y_lstm)

        # Ölceklendir
        X_lstm_scaled = scaler.transform(X_lstm.reshape(-1, len(feature_cols)))
        X_lstm = X_lstm_scaled.reshape(X_lstm.shape)

        split_idx = int(len(X_lstm) * 0.8)
        X_tr, X_te = X_lstm[:split_idx], X_lstm[split_idx:]
        y_tr, y_te = y_lstm[:split_idx], y_lstm[split_idx:]

        print(f"  LSTM veri: Train={X_tr.shape[0]}, Test={X_te.shape[0]}")

        # Model
        lstm_model = Sequential([
            LSTM(64, return_sequences=True, input_shape=(timesteps, len(feature_cols))),
            Dropout(0.2),
            LSTM(32, return_sequences=False),
            Dropout(0.2),
            Dense(16, activation='relu'),
            Dense(1)
        ])
        lstm_model.compile(optimizer='adam', loss='mse')
        lstm_model.summary(print_fn=lambda x: None)  # Sessiz

        early_stop = EarlyStopping(
            monitor='val_loss', patience=10, restore_best_weights=True
        )

        history = lstm_model.fit(
            X_tr, y_tr,
            validation_split=0.1,
            epochs=100,
            batch_size=32,
            callbacks=[early_stop],
            verbose=0
        )

        y_pred_lstm = lstm_model.predict(X_te, verbose=0).flatten()
        mae_lstm = mean_absolute_error(y_te, y_pred_lstm)
        r2_lstm = r2_score(y_te, y_pred_lstm)

        print(f"\n  LSTM Performansi (Test):")
        print(f"    MAE:  {mae_lstm:.2f} dongu")
        print(f"    R²:   {r2_lstm:.4f}")

        # LSTM modelini kaydet
        lstm_model.save('models/predictive_maintenance_lstm.h5')
        print("  -> models/predictive_maintenance_lstm.h5")
    else:
        print("  Yetersiz LSTM verisi, atlanıyor.")

except ImportError:
    print("  TensorFlow yok, LSTM atlandi.")
except Exception as e:
    print(f"  LSTM hatasi: {e}")
    print("  Random Forest yeterli, devam ediliyor...")

# =========================================================================
# 6. Modelleri Kaydetme
# =========================================================================
print("\n[6/7] Modeller kaydediliyor...")

os.makedirs('models', exist_ok=True)

# Random Forest Regresyon
joblib.dump(rf_reg, 'models/random_forest_rul.pkl')
print("  -> models/random_forest_rul.pkl")

# Scaler
joblib.dump(scaler, 'models/scaler.pkl')
print("  -> models/scaler.pkl")

# Random Forest Siniflandirma
joblib.dump(rf_clf, 'models/random_forest_anomaly.pkl')
print("  -> models/random_forest_anomaly.pkl")

# ONNX formatinda disa aktar (C# entegrasyonu icin)
try:
    from skl2onnx import convert_sklearn
    from skl2onnx.common.data_types import FloatTensorType

    initial_type = [('float_input', FloatTensorType([None, len(feature_cols)]))]
    onnx_model = convert_sklearn(rf_reg, initial_types=initial_type)

    with open("models/random_forest_rul.onnx", "wb") as f:
        f.write(onnx_model.SerializeToString())

    print("  -> models/random_forest_rul.onnx (C# uyumlu)")
except Exception as e:
    print(f"  ONNX export hatasi: {e}")
    print("  ONNX olmadan devam ediliyor, .pkl dosyalari yeterli.")

# =========================================================================
# 7. Ozellik Onemliligi - Anomali Modeli
# =========================================================================
print("\n[7/7] Anomali modeli ozellik onemliligi...")

clf_importances = rf_clf.feature_importances_
clf_feat_imp = pd.Series(clf_importances, index=feature_cols).sort_values(ascending=False)

print("  Anomali tespiti icin en onemli 10 feature:")
for i, (feat, imp) in enumerate(clf_feat_imp.head(10).items(), 1):
    print(f"    {i:2d}. {feat:30s} {imp:.4f}")

# =========================================================================
# Özet
# =========================================================================
print("\n" + "=" * 60)
print("MODEL EGITIMI TAMAMLANDI!")
print("=" * 60)

print(f"\nModel Performans Özeti:")
print(f"  Random Forest Regresyon: MAE={mae_reg:.2f}, R²={r2_reg:.4f}")
print(f"  Random Forest Anomali:   Accuracy={classification_report(y_test_cl, y_pred_cl, target_names=['Normal','Degraded']).strip()}")

print(f"\nKaydedilen dosyalar:")
for f in os.listdir('models'):
    size = os.path.getsize(f'models/{f}')
    print(f"  models/{f} ({size/1024:.1f} KB)")

print(f"\nGrafikler:")
for f in os.listdir('figures'):
    size = os.path.getsize(f'figures/{f}')
    print(f"  figures/{f} ({size/1024:.1f} KB)")

print("\nSonraki adim: model_predictor.py (Hafta 3)")
