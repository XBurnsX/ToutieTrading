"""
debug_candle_compare.py — Compare MT5 vs DuckDB vs Chart pour chaque bougie.

Lance depuis le dossier python_bridge OU depuis la racine ToutieTrading :
    python python_bridge/debug_candle_compare.py
    python python_bridge/debug_candle_compare.py EURUSD 2026-04-05 2026-04-06

Paramètres optionnels (défaut = AUDCAD 2026-04-01 2026-04-15) :
    SYMBOL    FROM_DATE    TO_DATE
"""

import sys
import traceback
from datetime import datetime, timezone, timedelta
from pathlib import Path

# ── Check imports ──────────────────────────────────────────────────────────────
try:
    import MetaTrader5 as mt5
except ImportError:
    print("ERREUR : package MetaTrader5 non installé.")
    print("  > pip install MetaTrader5")
    sys.exit(1)

try:
    import duckdb
except ImportError:
    print("ERREUR : package duckdb non installé.")
    print("  > pip install duckdb")
    sys.exit(1)

# ── Paramètres ────────────────────────────────────────────────────────────────
QUEBEC_TZ = timezone(timedelta(hours=-4))  # EDT (avril-octobre)

TFS = {
    "M1":  (mt5.TIMEFRAME_M1,   60),
    "M5":  (mt5.TIMEFRAME_M5,  300),
    "M15": (mt5.TIMEFRAME_M15, 900),
    "H1":  (mt5.TIMEFRAME_H1, 3600),
    "H4":  (mt5.TIMEFRAME_H4, 14400),
    "D":   (mt5.TIMEFRAME_D1, 86400),
}


def to_chart_unix(utc_ts: int) -> int:
    """Reproduit C# TimeZoneHelper.ToChartUnixSeconds — fake UTC depuis QC wall-clock."""
    qc = datetime.fromtimestamp(utc_ts, tz=QUEBEC_TZ)
    fake = datetime(qc.year, qc.month, qc.day,
                    qc.hour, qc.minute, qc.second, tzinfo=timezone.utc)
    return int(fake.timestamp())


def main():
    # ── Arguments avec défauts ────────────────────────────────────────────────
    symbol   = sys.argv[1] if len(sys.argv) > 1 else "AUDCAD"
    from_str = sys.argv[2] if len(sys.argv) > 2 else "2026-04-01"
    to_str   = sys.argv[3] if len(sys.argv) > 3 else "2026-04-15"

    print(f"=== debug_candle_compare.py ===")
    print(f"  Symbole : {symbol}")
    print(f"  Période : {from_str}  →  {to_str}")
    print()

    try:
        from_dt = datetime.strptime(from_str, "%Y-%m-%d").replace(tzinfo=timezone.utc)
        to_dt   = datetime.strptime(to_str,   "%Y-%m-%d").replace(
            hour=23, minute=59, second=59, tzinfo=timezone.utc)
    except ValueError as e:
        print(f"ERREUR date : {e}")
        print("Format attendu : YYYY-MM-DD")
        sys.exit(1)

    # ── MT5 ───────────────────────────────────────────────────────────────────
    print("Connexion MT5...")
    if not mt5.initialize():
        print("ERREUR : MT5 non disponible. Lance MetaTrader 5 et réessaie.")
        sys.exit(1)
    print("  MT5 OK")

    # ── DuckDB ────────────────────────────────────────────────────────────────
    script_dir = Path(__file__).resolve().parent
    # Chercher candles.db en remontant depuis python_bridge/
    db_path = None
    for parent in [script_dir, *script_dir.parents]:
        candidate = parent / "data" / "candles.db"
        if candidate.exists():
            db_path = candidate
            break

    if db_path is None:
        print("ERREUR : candles.db introuvable (cherché dans data/ depuis python_bridge jusqu'à la racine).")
        mt5.shutdown()
        sys.exit(1)

    print(f"  DuckDB : {db_path}")
    con = duckdb.connect(str(db_path), read_only=True)

    # ── Log output ────────────────────────────────────────────────────────────
    log_dir = db_path.parent.parent / "logs"
    log_dir.mkdir(exist_ok=True)
    log_path = log_dir / "candle_compare.log"
    print(f"  Log    : {log_path}")
    print()

    with open(log_path, "w", encoding="utf-8") as f:

        def w(line=""):
            f.write(line + "\n")
            print(line)

        w(f"=== Candle Comparison : {symbol}  |  {from_str} → {to_str} ===")
        w(f"Généré le : {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        w("=" * 130)

        total_match     = 0
        total_mismatch  = 0
        total_mt5_only  = 0
        total_db_only   = 0

        for tf_name, (tf_code, _tf_sec) in TFS.items():

            w()
            w("=" * 130)
            w(f"  TIMEFRAME : {tf_name}")
            w("=" * 130)

            # ── MT5 ──────────────────────────────────────────────────────────
            try:
                rates = mt5.copy_rates_range(symbol, tf_code, from_dt, to_dt)
            except Exception as e:
                w(f"  ERREUR MT5 copy_rates_range : {e}")
                rates = None

            mt5_map: dict[int, dict] = {}
            if rates is not None:
                for r in rates:
                    mt5_map[int(r["time"])] = {
                        "O": round(float(r["open"]),  5),
                        "H": round(float(r["high"]),  5),
                        "L": round(float(r["low"]),   5),
                        "C": round(float(r["close"]), 5),
                        "V": int(r["tick_volume"]),
                    }

            # ── DuckDB ───────────────────────────────────────────────────────
            try:
                rows = con.execute("""
                    SELECT time, open, high, low, close, volume
                    FROM candles
                    WHERE symbol = ? AND timeframe = ?
                      AND time >= ? AND time <= ?
                    ORDER BY time
                """, [symbol, tf_name, from_dt, to_dt]).fetchall()
            except Exception as e:
                w(f"  ERREUR DuckDB : {e}")
                rows = []

            db_map: dict[int, dict] = {}
            for row in rows:
                t = row[0]
                unix_ts = int(t.timestamp()) if hasattr(t, "timestamp") else int(t)
                db_map[unix_ts] = {
                    "O": round(float(row[1]), 5),
                    "H": round(float(row[2]), 5),
                    "L": round(float(row[3]), 5),
                    "C": round(float(row[4]), 5),
                    "V": int(row[5]),
                }

            # ── Comparaison ──────────────────────────────────────────────────
            all_keys = sorted(set(list(mt5_map.keys()) + list(db_map.keys())))

            tf_match    = 0
            tf_mismatch = 0
            tf_mt5_only = 0
            tf_db_only  = 0

            # Colonne header
            HDR = (f"{'UTC':>22}  {'QC (EDT-4)':>19}  {'ChartUnix':>12}  "
                   f"{'Src':4}  {'O':>10}  {'H':>10}  {'L':>10}  "
                   f"{'C':>10}  {'V':>8}  {'Dir':3}  Statut")
            w(HDR)
            w("-" * 130)

            for k in all_keys:
                utc_str = datetime.fromtimestamp(k, tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
                qc_str  = datetime.fromtimestamp(k, tz=QUEBEC_TZ).strftime("%m-%d %H:%M")
                ch_ts   = to_chart_unix(k)

                m = mt5_map.get(k)
                d = db_map.get(k)

                if m and d:
                    ok = (m["O"] == d["O"] and m["H"] == d["H"]
                          and m["L"] == d["L"] and m["C"] == d["C"])
                    if ok:
                        tf_match += 1
                        dr = "UP" if m["C"] >= m["O"] else "DN"
                        f.write(               # vers fichier seulement (pas console)
                            f"{utc_str:>22}  {qc_str:>19}  {ch_ts:>12}  "
                            f"{'BOT':4}  {m['O']:>10.5f}  {m['H']:>10.5f}  "
                            f"{m['L']:>10.5f}  {m['C']:>10.5f}  {m['V']:>8}  "
                            f"{dr:3}  OK\n"
                        )
                    else:
                        tf_mismatch += 1
                        w(f"{utc_str:>22}  {qc_str:>19}  {ch_ts:>12}  "
                          f"{'MT5':4}  {m['O']:>10.5f}  {m['H']:>10.5f}  "
                          f"{m['L']:>10.5f}  {m['C']:>10.5f}  {m['V']:>8}  "
                          f"{'':3}  *** MISMATCH ***")
                        w(f"{'':>22}  {'':>19}  {'':>12}  "
                          f"{'DB':4}  {d['O']:>10.5f}  {d['H']:>10.5f}  "
                          f"{d['L']:>10.5f}  {d['C']:>10.5f}  {d['V']:>8}")

                elif m and not d:
                    tf_mt5_only += 1
                    f.write(
                        f"{utc_str:>22}  {qc_str:>19}  {ch_ts:>12}  "
                        f"{'MT5':4}  {m['O']:>10.5f}  {m['H']:>10.5f}  "
                        f"{m['L']:>10.5f}  {m['C']:>10.5f}  {m['V']:>8}  "
                        f"{'':3}  MT5-ONLY\n"
                    )

                elif d and not m:
                    tf_db_only += 1
                    f.write(
                        f"{utc_str:>22}  {qc_str:>19}  {ch_ts:>12}  "
                        f"{'DB':4}  {d['O']:>10.5f}  {d['H']:>10.5f}  "
                        f"{d['L']:>10.5f}  {d['C']:>10.5f}  {d['V']:>8}  "
                        f"{'':3}  DB-ONLY\n"
                    )

            total_match    += tf_match
            total_mismatch += tf_mismatch
            total_mt5_only += tf_mt5_only
            total_db_only  += tf_db_only

            w(f"\n  {tf_name} : {len(all_keys)} bougies total | "
              f"Match={tf_match} | MISMATCH={tf_mismatch} | "
              f"MT5-only={tf_mt5_only} | DB-only={tf_db_only}")

        # ── Résumé global ─────────────────────────────────────────────────────
        w()
        w("=" * 130)
        w("  RÉSUMÉ GLOBAL")
        w("=" * 130)
        w(f"  Match    : {total_match}")
        w(f"  MISMATCH : {total_mismatch}  {'<-- PROBLÈMES ICI' if total_mismatch else '(aucun — données identiques)'}")
        w(f"  MT5-only : {total_mt5_only}  (bougies MT5 hors range DuckDB — normal si warmup non téléchargé)")
        w(f"  DB-only  : {total_db_only}")
        w()
        if total_mismatch == 0:
            w("  CONCLUSION : Les données MT5 et DuckDB sont IDENTIQUES.")
            w("  Le problème visuel est dans le RENDU du chart, pas dans les données.")

    con.close()
    mt5.shutdown()
    print()
    print(f"Log complet : {log_path}")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        print("\n=== ERREUR NON GÉRÉE ===")
        traceback.print_exc()
        input("\nAppuie sur Entrée pour fermer...")
        sys.exit(1)
