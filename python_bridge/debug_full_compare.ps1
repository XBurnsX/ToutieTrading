# ToutieTrader — Comparaison COMPLÈTE : MT5 vs DuckDB vs Chart
#
# ÉTAPES :
#   1. Lance le replay dans l'app (Start)
#   2. Laisse défiler quelques minutes de bougies
#   3. Stop le replay
#   4. Double-clique sur CE fichier .ps1
#   5. Ouvre logs/full_compare.log

$SYMBOL    = "AUDCAD"
$FROM_DATE = "2026-04-01"
$TO_DATE   = "2026-04-15"

# ─────────────────────────────────────────────────────────────────────────────
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "=== ToutieTrader — Full Candle Compare ===" -ForegroundColor Yellow
Write-Host "  MT5  vs  DuckDB  vs  Chart (chart_candles.log)" -ForegroundColor Cyan
Write-Host ""

pip install duckdb pandas --quiet 2>&1 | Out-Null

python debug_full_compare.py $SYMBOL $FROM_DATE $TO_DATE

Write-Host ""
Write-Host "Appuie sur Entree pour fermer..." -ForegroundColor Gray
Read-Host
