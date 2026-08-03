"""ONNX export — SCaDa RUL modeli (S8)"""
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType
import joblib
import numpy as np
import os
import onnxruntime as ort

# SCaDa RUL modelini yükle
MODEL_DIR = "models/SCaDa"
model_path = os.path.join(MODEL_DIR, "random_forest_rul.pkl")
rf = joblib.load(model_path)
n_features = rf.n_features_in_  # Gerçek feature count'u modelden oku (216)

print(f"SCaDa RUL modeli: {n_features} feature")

initial_type = [('float_input', FloatTensorType([None, n_features]))]
onnx_model = convert_sklearn(rf, initial_types=initial_type)

output_path = os.path.join(MODEL_DIR, "random_forest_rul.onnx")
with open(output_path, 'wb') as f:
    f.write(onnx_model.SerializeToString())

size = os.path.getsize(output_path)
print(f'ONNX model kaydedildi: {output_path} ({size/1024:.1f} KB)')

# Test: ONNX vs Python
sess = ort.InferenceSession(output_path)
test_input = np.random.randn(1, n_features).astype(np.float32)
pred_onnx = sess.run(None, {'float_input': test_input})[0]
pred_python = rf.predict(test_input)

print(f'ONNX tahmin (ilk 3): {pred_onnx[0][:3]}')
print(f'Python tahmin (ilk 3): {pred_python[:3]}')
print(f'Maks fark: {np.abs(pred_onnx[0] - pred_python).max():.6f}')
print('ONNX test BASARILI!')
