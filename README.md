# SCaDa Kestirimci Bakim Sistemi

Yapay zeka destekli SCaDa sistemi icin kestirimci bakim motoru.

## Proje Ozeti

Bu proje, fabrika SCaDa sistemlerindeki makinelerin saglik durumunu analiz eder, kalan有用 omru (RUL) tahmin eder ve olasi arizalari önceden tespit eder.

### Takim

| Kisi | Rol | Sorumluluk |
|------|-----|------------|
| **Kisi 1** | AI & Veri Bilimi | Zaman serisi, anomali tespiti, model egitimi |
| **Kisi 2** | Veri Akisi & Linux | MQTT, sensor streaming, Linux sunucu |
| **Kisi 3** | C# Masaustu UI | Dashboard, görsellestirme, alarm sistemi |
| **Kisi 4** | DB Mimarisi | SQL Server, tablo tasarimi, sorgu optimizasyonu |

## Hizi Baslangic

```powershell
# 1. Ortam olustur
cd SCaDa_predictive_maintenance
python -m venv SCaDa_venv
SCaDa_venv\Scripts\activate

# 2. Paketleri yukle
pip install numpy pandas matplotlib seaborn scikit-learn tensorflow joblib onnx onnxruntime skl2onnx

# 3. Veri olustur (sentetik)
python generate_synthetic_data.py

# 4. EDA yap
python eda_script.py

# 5. Model egit
python model_training.py

# 6. Tahmin API test
python src\model_predictor.py

# 7. Test calistir
python tests\test_predictions.py
```

## Proje Yapisi

```
SCaDa_predictive_maintenance/
├── notebooks/
│   └── 01_eda.ipynb              # EDA Notebook
├── src/
│   └── model_predictor.py        # Tahmin API (Hafta 3)
├── models/                       # Egilitmis model dosyalari
│   ├── random_forest_rul.pkl
│   ├── random_forest_anomaly.pkl
│   ├── scaler.pkl
│   ├── random_forest_rul.onnx
│   └── README.md
├── data/
│   └── FD001/                    # NASA C-MAPSS verisi
│       ├── train.txt
│       ├── test.txt
│       └── RUL.txt
├── tests/
│   └── test_predictions.py       # Test senaryolari (Hafta 4)
├── docs/
│   └── MODEL_CARD.md             # Model dokumantasyonu
├── figures/                      # EDA grafikleri
├── generate_synthetic_data.py    # Veri uretici
├── eda_script.py                 # EDA scripti
├── model_training.py             # Model egitimi (Hafta 2)
├── export_onnx.py                # ONNX export
└── README.md
```

## Haftalik Teslim Plan

### Hafta 1: EDA (Tamamlandi)
- [x] Veri yukleme ve inceleme
- [x] Sensör trend analizi
- [x] Bozulma desen tespiti
- [x] Ozellik muhendisligi (kaydirma pencere)
- [x] Temizlenmis veri seti

### Hafta 2: Model Egıtimi (Tamamlandi)
- [x] Random Forest Regresyon (RUL Tahmini) - MAE=32.65, R²=0.67
- [x] Random Forest Siniflandirma (Anomali) - Accuracy=%98
- [x] Model kayit (.pkl, .onnx)
- [x] Ozellik onemliligi analizi

### Hafta 3: Tahmin API (Tamamlandi)
- [x] Predictor sinifi
- [x] predict() fonksiyonu
- [x] predict_batch() fonksiyonu
- [x] Eksik sensör desteği
- [x] JSON serilestirme

### Hafta 4: Test ve Dokumantasyon (Tamamlandi)
- [x] 23 test senaryosu (22/23 geçti)
- [x] MODEL_CARD.md
- [x] MODELS/README.md

## Model Performans Özeti

| Model | Metrik | Deger |
|-------|--------|-------|
| RF Regresyon | MAE | 32.65 dongu |
| RF Regresyon | R² | 0.6700 |
| RF Siniflandirma | Accuracy | %98 |
| RF Siniflandirma | F1 (Degraded) | 0.94 |

## ONNX ile C# Entegrasyonu

C# takimi (Kisi 3) ONNX modelini kullanabilir:

```csharp
var session = new InferenceSession("models/random_forest_rul.onnx");
var inputs = new Dictionary<string, Tensor>
{
    { "float_input", new DenseTensor<float>(features) }
};
var results = session.Run(inputs);
```

## Lisans

Proje egitim/amacli olarak gelistirilmistir.
