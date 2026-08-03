"""
train_SCaDa_model.py — S7: SCaDa modeli eğit (Faz 5)
========================================================

RUL regresyonu + fault_mode sınıflandırıcı, SCaDa alan adlarıyla.
Çoklu ölçek pencere (10/50/200) + op-koşullu residual özellikler.

Çıktı:
  models/SCaDa/random_forest_rul.pkl      — RUL regresyon modeli
  models/SCaDa/random_forest_fault.pkl    — fault_mode sınıflandırıcı
  models/SCaDa/scaler.pkl                 — feature scaler
  models/SCaDa/feature_cols.json          — giriş sözleşmesi
"""
from __future__ import annotations

import json
import sys
import io
import os
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.ensemble import RandomForestRegressor, RandomForestClassifier
from sklearn.preprocessing import StandardScaler
from sklearn.model_selection import GroupShuffleSplit, train_test_split
from sklearn.metrics import mean_absolute_error, r2_score, classification_report
import joblib

# ─── Yönergeler ──────────────────────────────────────────────────────

ROOT = Path(__file__).parent.resolve()
sys.path.insert(0, str(ROOT))

DATA_PATH = ROOT / "data" / "train_SCaDa.csv"
MODEL_DIR = ROOT / "models" / "SCaDa"
MODEL_DIR.mkdir(parents=True, exist_ok=True)

# Sensör kolonları (CSV'de var)
SENSOR_COLS = [
    "s_1_suction_pressure_bar",
    "s_2_discharge_pressure_bar",
    "s_3_pressure_ratio",
    "s_4_suction_temp_c",
    "s_5_discharge_temp_c",
    "s_6_shaft_rpm",
    "s_7_vibration_de_mm_s",
    "s_8_vibration_nde_mm_s",
    "s_9_axial_displacement_mm",
    "s_10_bearing_temp_1_c",
    "s_11_bearing_temp_2_c",
    "s_12_lube_oil_pressure_bar",
    "s_13_lube_oil_temp_c",
    "s_14_gas_flow_meter_m3_h",
    "s_15_seal_gas_pressure_bar",
    "s_16_gas_flow_m3_h",
    "s_17_power_mw",
    "s_18_polytropic_efficiency",
    "s_19_surge_margin_pct",
    "s_20_filter_dp_bar",
    "s_21_torque_nm",
]

# Op kolonları (snapshot'tan alınacak, korpus CSV'de yok → 0 ile doldurulur)
OP_COLS = ["op_1_ambient_temp_c", "op_2_inlet_pressure_bar",
           "op_3_flow_demand_m3_h", "op_4_speed_setpoint_pct"]

# RUL normalize sabiti (degradation.py'daki _get_dynamic_max_rul ile uyumlu)
MAX_RUL = 157680.0


# ─── Özellik mühendisliği ──────────────────────────────────────────

def build_features(df: pd.DataFrame, window_sizes: list[int] = [3, 10, 30]) -> tuple[np.ndarray, list[str]]:
    """
    Çoklu ölçek pencere + op-koşullu residual özellikler oluştur.

    Özellik seti:
      1. Ham sensör değerleri (21)
      2. Op ayarları (4) — korpus CSV'de yok, 0 ile doldurulur
      3. Rolling mean/std/residual × 3 window (3/10/30) × 21 sensör = 3 × 3 × 21 = 189

    Toplam: 21 + 4 + 189 = 214 feature

    SZ1 Fix: health_score feature'dan çıkarıldı — hedef sızıntısı.
    """
    features = []
    feature_names = []

    # 1. Ham sensörler
    for col in SENSOR_COLS:
        val = df[col].fillna(0).values.astype(np.float32)
        features.append(val)
        feature_names.append(col)

    # 2. Op ayarları (korpus CSV'de yok → 0)
    for op in OP_COLS:
        features.append(np.zeros(len(df), dtype=np.float32))
        feature_names.append(op)

    # 3-4. Rolling özellikler (her window için mean, std, residual)
    # KRİTİK: rolling ünite bazında gruplanır — yoksa korpusta üniteler peş peşe
    # olduğu için window bir sonraki ünitenin karelerini ortalar (cross-unit
    # kontaminasyon) → eğitim kirli feature öğrenir, tek-ünite çıkarımı uyuşmaz.
    has_unit = "unit_id" in df.columns
    for w in window_sizes:
        for col in SENSOR_COLS:
            series = df[col].ffill().fillna(0)

            if has_unit:
                grp = series.groupby(df["unit_id"], sort=False)
                rmean = grp.transform(lambda x: x.rolling(w, min_periods=1).mean())
                rstd = grp.transform(lambda x: x.rolling(w, min_periods=1).std())
            else:
                rmean = series.rolling(window=w, min_periods=1).mean()
                rstd = series.rolling(window=w, min_periods=1).std()

            rmean = rmean.values.astype(np.float32)
            rstd = np.nan_to_num(rstd.values.astype(np.float32))

            features.append(rmean)
            feature_names.append(f"{col}_rmean_{w}")

            features.append(rstd)
            feature_names.append(f"{col}_rstd_{w}")

            # Residual: sensör - beklenen (rolling mean)
            residual = (series.values.astype(np.float32) - rmean)
            features.append(residual)
            feature_names.append(f"{col}_residual_{w}")

    # 5. cycle (SZ1: health_score feature'dan çıkarıldı — hedef sızıntısı)
    # SZ3: cycle da RUL ile neredeyse doğrusal (RUL = failure_cycle - cycle),
    #      onu da feature'dan çıkarıyoruz — model sensörlere göre tahmin yapacak

    X = np.column_stack(features)
    return X, feature_names


# ─── Eğitim ──────────────────────────────────────────────────────────

def train_health_model(X: np.ndarray, y_health: np.ndarray, feature_names: list[str], unit_ids: pd.Series):
    """Health score regresyonu — smooth 1→0 azalma (A1 Fix).

    RUL yerine health_score tahmin ediyoruz çünkü:
      - RUL step fonksiyon (plateau → düşüş) → RF ogrenemez
      - Health score smooth degisim → model dogru ogrenir
      - predict_service health_score'dan RUL'u cevirir
    
    A1 Fix: Unit-based split — bazı ünitelerin TAM döngüsü train, digerleri test.
    Böylece model training sırasında ariza orneklerini de gorecek.
    """
    print("\n[Health Score Regresyonu] Eğitiliyor...")

    # A1 Fix: Unit-based split — görülmeyen üniteler test'te
    all_unit_ids = unit_ids.unique().tolist()
    np.random.seed(42)
    np.random.shuffle(all_unit_ids)
    
    n_train_units = int(len(all_unit_ids) * 0.75)
    train_units = set(all_unit_ids[:n_train_units])
    test_units = set(all_unit_ids[n_train_units:])
    
    all_train_idx = []
    all_test_idx = []
    
    for uid in unit_ids.unique():
        mask = (unit_ids == uid).values
        indices = np.where(mask)[0]
        if uid in train_units:
            all_train_idx.extend(indices.tolist())
        else:
            all_test_idx.extend(indices.tolist())

    X_train, X_test = X[all_train_idx], X[all_test_idx]
    y_train, y_test = y_health[all_train_idx], y_health[all_test_idx]

    print(f"  Train units: {len(train_units)}, Test units: {len(test_units)}")
    print(f"  Train: {len(y_train)} örnek ({X_train.shape[1]} feature)")
    print(f"  Test:  {len(y_test)} örnek")
    print(f"  Train health dist: healthy={np.sum(y_train>=0.7)}, degrading={(y_train<0.7)&(y_train>=0.3)}, critical={np.sum(y_train<0.3)}")

    scaler = StandardScaler()
    X_train_sc = scaler.fit_transform(X_train)
    X_test_sc = scaler.transform(X_test)

    rf = RandomForestRegressor(
        n_estimators=150,
        max_depth=20,
        min_samples_leaf=10,
        random_state=42,
        n_jobs=-1,
    )
    rf.fit(X_train_sc, y_train)

    # Değerlendirme
    y_pred = rf.predict(X_test_sc)
    mae = mean_absolute_error(y_test, y_pred)
    r2 = r2_score(y_test, y_pred)
    corr = np.corrcoef(y_test, y_pred)[0, 1]
    print(f"  MAE: {mae:.4f}")
    print(f"  R²:  {r2:.4f}")
    print(f"  Korelasyon: {corr:.4f}")
    print(f"  Test seti: {len(y_test)} örnek")

    # Feature importance
    importances = rf.feature_importances_
    top_idx = np.argsort(importances)[-10:][::-1]
    print("  En önemli 10 feature:")
    for i in top_idx:
        print(f"    {feature_names[i]:40s} {importances[i]:.4f}")

    return rf, scaler


def train_rul_model(X: np.ndarray, y_rul: np.ndarray, feature_names: list[str], unit_ids: pd.Series):
    """RUL (kalan omur) regresyonu — health_score'a ozel parametreler degil.

    RUL'un dogasi farkli: plateau (~80% cycle yuksek), sonra ani dusus.
    Health score smooth azalir, RUL step gibi davranir → RF parametreleri farkli olmal.
    
    A1 Fix: Unit-based split — train_health_model ile ayni mantik (0.75 oran).
    """
    print("\n[RUL Regresyonu] Egitiliyor...")

    all_unit_ids = unit_ids.unique().tolist()
    np.random.seed(42)
    np.random.shuffle(all_unit_ids)
    
    n_train_units = int(len(all_unit_ids) * 0.75)
    train_units = set(all_unit_ids[:n_train_units])
    test_units = set(all_unit_ids[n_train_units:])
    
    all_train_idx = []
    all_test_idx = []
    
    for uid in unit_ids.unique():
        mask = (unit_ids == uid).values
        indices = np.where(mask)[0]
        if uid in train_units:
            all_train_idx.extend(indices.tolist())
        else:
            all_test_idx.extend(indices.tolist())

    X_train, X_test = X[all_train_idx], X[all_test_idx]
    y_train, y_test = y_rul[all_train_idx], y_rul[all_test_idx]

    print(f"  Train units: {len(train_units)}, Test units: {len(test_units)}")
    print(f"  Train: {len(y_train)} ornek ({X_train.shape[1]} feature)")
    print(f"  Test:  {len(y_test)} ornek")
    print(f"  RUL train dist: min={y_train.min():.0f}, max={y_train.max():.0f}")

    scaler = StandardScaler()
    X_train_sc = scaler.fit_transform(X_train)
    X_test_sc = scaler.transform(X_test)

    # RUL icin daha derin agac — plateau+dusus pattern'ini ogrenebilsin
    rf = RandomForestRegressor(
        n_estimators=150,
        max_depth=20,
        min_samples_leaf=10,
        random_state=42,
        n_jobs=-1,
    )
    rf.fit(X_train_sc, y_train)

    # Degerlendirme
    y_pred = rf.predict(X_test_sc)
    mae = mean_absolute_error(y_test, y_pred)
    r2 = r2_score(y_test, y_pred)
    corr = np.corrcoef(y_test, y_pred)[0, 1]
    print(f"  MAE: {mae:.4f}")
    print(f"  R²:  {r2:.4f}")
    print(f"  Korelasyon: {corr:.4f}")
    print(f"  Test seti: {len(y_test)} ornek")

    # Feature importance
    importances = rf.feature_importances_
    top_idx = np.argsort(importances)[-10:][::-1]
    print("  En onemli 10 feature:")
    for i in top_idx:
        print(f"    {feature_names[i]:40s} {importances[i]:.4f}")

    return rf, scaler


def train_health_state_model(X: np.ndarray, y_state: pd.Series, feature_names: list[str], unit_ids: pd.Series):
    """Health state sınıflandırma (healthy/degrading/critical) — A1 Fix."""
    print("\n[Health State Sınıflandırma] Eğitiliyor...")

    # Unit-based split — train_health_model ile aynı mantık (0.75 oran)
    all_unit_ids = unit_ids.unique().tolist()
    np.random.seed(42)
    np.random.shuffle(all_unit_ids)
    
    n_train_units = int(len(all_unit_ids) * 0.75)
    train_units = set(all_unit_ids[:n_train_units])
    test_units = set(all_unit_ids[n_train_units:])
    
    all_train_idx = []
    all_test_idx = []
    
    for uid in unit_ids.unique():
        mask = (unit_ids == uid).values
        indices = np.where(mask)[0]
        if uid in train_units:
            all_train_idx.extend(indices.tolist())
        else:
            all_test_idx.extend(indices.tolist())

    X_train, X_test = X[all_train_idx], X[all_test_idx]
    y_train, y_test = y_state.iloc[all_train_idx], y_state.iloc[all_test_idx]

    print(f"  Train units: {len(train_units)}, Test units: {len(test_units)}")
    print(f"  Train: {len(y_train)} örnek ({X_train.shape[1]} feature)")
    print(f"  Test:  {len(y_test)} örnek")
    print(f"  Dagilim: {y_test.value_counts().to_dict()}")

    scaler = StandardScaler()
    X_train_sc = scaler.fit_transform(X_train)
    X_test_sc = scaler.transform(X_test)

    rf = RandomForestClassifier(
        n_estimators=300,
        max_depth=50,
        min_samples_leaf=5,
        random_state=42,
        n_jobs=-1,
    )
    rf.fit(X_train_sc, y_train)

    y_pred = rf.predict(X_test_sc)
    acc = rf.score(X_test_sc, y_test)
    print(f"  Accuracy: {acc:.4f}")
    print("\n  Classification Report:")
    print(classification_report(y_test, y_pred, zero_division=0))

    return rf, scaler


def train_fault_model(X: np.ndarray, y_fault: pd.Series, feature_names: list[str], unit_ids: pd.Series):
    """fault_mode sınıflandırıcı eğit.

    S11 Fix: Artık her fault_mode'da ≥5 ünite var → GroupShuffleSplit(groups=unit_id)
    çalışır! Test seti görülmemiş ünitelerden oluşur — gerçek senaryo.
    
    Önceki: stratified split (her ünite tek mod, 4 ünite → ezber).
    Şimdi: her modda 6-7 ünite → GroupShuffleSplit ile test'te görülmemiş üniteler.
    """
    print("\n[Fault Mode Sınıflandırma] Eğitiliyor...")

    # Sadece arızalı örnekleri al (healthy yok) — NaN temizle
    fault_mask = (y_fault != "") & y_fault.notna()
    X_fault = X[fault_mask]
    y_fault_cls = y_fault[fault_mask].astype(str)
    unit_ids_fault = unit_ids[fault_mask]

    print(f"  Arızalı örnek: {len(y_fault_cls)}")
    print(f"  Dağılım: {y_fault_cls.value_counts().to_dict()}")
    print(f"  Ayrık ünite sayısı: {unit_ids_fault.nunique()}")

    if len(y_fault_cls) == 0:
        raise ValueError("Arızalı örnek bulunamadı!")

    # S11 Fix: GroupShuffleSplit — görülmemiş üniteler test'te
    gss = GroupShuffleSplit(test_size=0.2, random_state=42)
    train_idx, test_idx = next(gss.split(X_fault, y_fault_cls, groups=unit_ids_fault))

    X_train, X_test = X_fault[train_idx], X_fault[test_idx]
    y_train = y_fault_cls.iloc[train_idx].values
    y_test = y_fault_cls.iloc[test_idx].values

    print(f"  Train: {len(y_train)} örnek ({X_train.shape[1]} feature)")
    print(f"  Test:  {len(y_test)} örnek")
    print(f"  Ayrık ünite sayısı — train: {unit_ids_fault.iloc[train_idx].nunique()}, test: {unit_ids_fault.iloc[test_idx].nunique()}")

    scaler = StandardScaler()
    X_train_sc = scaler.fit_transform(X_train)
    X_test_sc = scaler.transform(X_test)

    rf = RandomForestClassifier(
        n_estimators=300,
        max_depth=50,
        min_samples_leaf=5,
        random_state=42,
        n_jobs=-1,
    )
    rf.fit(X_train_sc, y_train)

    # Değerlendirme
    y_pred = rf.predict(X_test_sc)
    print(f"  Accuracy: {rf.score(X_test_sc, y_test):.4f}")
    print("\n  Classification Report:")
    print(classification_report(y_test, y_pred, zero_division=0))

    # Feature importance
    importances = rf.feature_importances_
    top_idx = np.argsort(importances)[-10:][::-1]
    print("  En önemli 10 feature:")
    for i in top_idx:
        print(f"    {feature_names[i]:40s} {importances[i]:.4f}")

    return rf, scaler


# ─── Ana ─────────────────────────────────────────────────────────────

def main():
    import warnings
    warnings.filterwarnings("ignore")

    # stdout UTF-8
    if sys.platform == "win32" and hasattr(sys.stdout, "buffer"):
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

    print("=" * 70)
    print("S7 — SCaDa Model Eğitimi (Faz 5)")
    print("=" * 70)

    # 1. Veri yükle
    print(f"\n[1/5] Veri yükleniyor: {DATA_PATH}")
    df = pd.read_csv(DATA_PATH, low_memory=False)
    print(f"  Satır: {len(df)}, Sütun: {len(df.columns)}")

    # 2. Özellik oluştur
    print("\n[2/5] Özellik mühendisliği (çoklu pencere + residual)...")
    X, feature_names = build_features(df)
    print(f"  Feature vektörü: {X.shape[1]} özellik, {X.shape[0]} örnek")

    # 3. Hedef değişkenler — A1 Fix: health_score + RUL + health_state + fault_mode
    y_health = df["health_score"].values.astype(np.float32)
    # RUL PIECEWISE CAP: erken-ömürdeki tahmin edilemez varyansı atar; predict_service
    # artık served RUL'u bu regresörden alıyor → arıza-yakını MAE 235→58 (held-out, 4×).
    # Healthy üniteler cap'i okur ("≥130 çevrim") — health_score×sabit'in şişirdiği
    # sahte 1330 yerine dürüst değer. C-MAPSS standardı piecewise-linear RUL (~125-130).
    RUL_CAP = 130
    y_rul = np.minimum(df["rul_cycles"].values, RUL_CAP).astype(np.float32)
    y_state = df["health_state"]
    y_fault = df["fault_mode"]

    print(f"  Health score dagilimi: min={y_health.min():.4f}, max={y_health.max():.4f}")
    print(f"  RUL cycles: min={y_rul.min():.0f}, max={y_rul.max():.0f}")
    print(f"  Health state dagilimi: {y_state.value_counts().to_dict()}")

    # 4. Modelleri egit — health_score regresyonu + RUL regresyonu + health_state siniflandirma + fault_mode
    print("\n[3/6] Health score regresyon modeli...")
    health_model, health_scaler = train_health_model(X, y_health, feature_names, df["unit_id"])

    print("\n[4/6] RUL (kalan omur) regresyonu...")
    rul_model, _ = train_rul_model(X, y_rul, feature_names, df["unit_id"])

    print("\n[5/6] Health state sınıflandırıcı...")
    health_state_model, health_state_scaler = train_health_state_model(X, y_state, feature_names, df["unit_id"])

    print("\n[6/6] Fault mode sınıflandırıcı...")
    fault_model, fault_scaler = train_fault_model(X, y_fault, feature_names, df["unit_id"])

    # 5. Kaydet — A1 Fix: dosya adlari icerikle eslesiyor
    print("\n[7/6] Modeller kaydediliyor...")
    joblib.dump(health_model, MODEL_DIR / "random_forest_health_score.pkl")
    joblib.dump(rul_model, MODEL_DIR / "random_forest_rul.pkl")
    joblib.dump(fault_model, MODEL_DIR / "random_forest_fault.pkl")
    joblib.dump(health_state_model, MODEL_DIR / "random_forest_health.pkl")
    joblib.dump(health_scaler, MODEL_DIR / "scaler.pkl")

    # Giriş sözleşmesi
    with open(MODEL_DIR / "feature_cols.json", "w") as f:
        json.dump({
            "n_features": X.shape[1],
            "feature_names": feature_names,
            "sensor_cols": SENSOR_COLS,
            "op_cols": OP_COLS,
            "window_sizes": [3, 10, 30],
        }, f, indent=2)

    print(f"  -> {MODEL_DIR / 'random_forest_rul.pkl'}")
    print(f"  -> {MODEL_DIR / 'random_forest_fault.pkl'}")
    print(f"  -> {MODEL_DIR / 'random_forest_health.pkl'}")
    print(f"  -> {MODEL_DIR / 'scaler.pkl'}")
    print(f"  -> {MODEL_DIR / 'feature_cols.json'}")

    # Model bilgisi
    print(f"\n  Health model: n_features_in_ = {health_model.n_features_in_}")
    print(f"  Fault model: n_features_in_ = {fault_model.n_features_in_}")
    print(f"  Health state model: n_features_in_ = {health_state_model.n_features_in_}")
    print(f"  Feature count: {X.shape[1]}")

    print("\n" + "=" * 70)
    print("Eğitim tamamlandı!")
    print("=" * 70)


if __name__ == "__main__":
    main()
