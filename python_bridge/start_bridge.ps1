# ToutieTrader — Démarrage du bridge Python MT5
# Lance uvicorn sur 127.0.0.1:8000

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "=== ToutieTrader MT5 Bridge ===" -ForegroundColor Yellow
Write-Host "URL : http://127.0.0.1:8000" -ForegroundColor Cyan
Write-Host "Docs : http://127.0.0.1:8000/docs" -ForegroundColor Cyan
Write-Host ""

uvicorn main:app --host 127.0.0.1 --port 8000 --reload
