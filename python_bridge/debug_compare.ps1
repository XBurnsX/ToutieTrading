# ToutieTrader — Debug comparaison bougies MT5 vs DuckDB vs Chart
# Double-clique pour lancer.  Modifie les 3 lignes ci-dessous si besoin.

$SYMBOL    = "AUDCAD"
$FROM_DATE = "2026-04-01"
$TO_DATE   = "2026-04-15"

# ─────────────────────────────────────────────────────────────────────────────
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "=== ToutieTrader — Candle Compare ===" -ForegroundColor Yellow
Write-Host "  Symbole : $SYMBOL" -ForegroundColor Cyan
Write-Host "  Periode : $FROM_DATE  ->  $TO_DATE" -ForegroundColor Cyan
Write-Host ""

# S'assure que duckdb et pandas sont installés
pip install duckdb pandas --quiet 2>&1 | Out-Null

python debug_candle_compare.py $SYMBOL $FROM_DATE $TO_DATE

Write-Host ""
Write-Host "Appuie sur Entree pour fermer..." -ForegroundColor Gray
Read-Host
