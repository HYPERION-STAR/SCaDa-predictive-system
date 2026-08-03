# SCaDa Model Servisi başlatıcı (Kişi 1)
# Kullanım:  sağ tık > "Run with PowerShell"  ya da  .\run_model.ps1
# Durdurmak için: bu pencerede Ctrl+C

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

# Windows konsolu Türkçe karakterlerde çökmesin diye UTF-8
$env:PYTHONUTF8 = "1"
$env:PYTHONIOENCODING = "utf-8"

# Ayarlanabilir (istersen değiştir):
# $env:SCaDa_LIVE_URL     = "http://100.114.223.5:8000/api/scada/live_data"  # Kişi 2 kaynağı
# $env:HUB_REFRESH_SECONDS = "5"                                             # arka plan yenileme aralığı

Write-Host "SCaDa Model Servisi baslatiliyor -> http://0.0.0.0:8010" -ForegroundColor Cyan
Write-Host "  /hub_state  = 24 unite toplu" -ForegroundColor DarkGray
Write-Host "  /health     = servis durumu" -ForegroundColor DarkGray
Write-Host "  Durdurmak icin Ctrl+C" -ForegroundColor DarkGray

python -m uvicorn predict_service:app --host 0.0.0.0 --port 8010
