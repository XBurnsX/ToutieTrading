"""
debug_full_compare.py — Compare MT5 vs DuckDB vs Chart (chart_candles.log) côte à côte.

ÉTAPES :
  1. Lance le replay dans l'app (Start)  → génère logs/chart_candles.log
  2. Arrête le replay
  3. Lance CE script (ou debug_full_compare.ps1)
  4. Ouvre logs/full_compare.log

Paramètres optionnels (défaut = AUDCAD 2026-04-01 2026-04-15) :
    python debug_full_compare.py [SYMBOL] [FROM_DATE] [TO_DATE]
"""

import sys
import re
import traceback
from datetime import datetime, timezone, timedelta
from pathlib import Path

try:
    import MetaTrader5 as mt5
except ImportError:
    print("ERREUR : pip install MetaTrader5"); sys.exit(1)
try:
    import duckdb
except ImportError:
    print("ERREUR : pip install duckdb"); sys.exit(1)

QUEBEC_TZ = timezone(timedelta(hours=-4))

TF_CODE = {
    "M1":  mt5.TIMEFRAME_M1,
    "M5":  mt5.TIMEFRAME_M5,
    "M15": mt5.TIMEFRAME_M15,
    "H1":  mt5.TIMEFRAME_H1,
    "H4":  mt5.TIMEFRAME_H4,
    "D":   mt5.TIMEFRAME_D1,
}


def to_chart_unix(utc_ts: int) -> int:
    qc = datetime.fromtimestamp(utc_ts, tz=QUEBEC_TZ)
    fake = datetime(qc.year, qc.month, qc.day,
                    qc.hour, qc.minute, qc.second, tzinfo=timezone.utc)
    return int(fake.timestamp())


def parse_chart_log(log_path: Path) -> dict[str, dict[int, dict]]:
    """
    Lit logs/chart_candles.log.
    Retourne { tf_name -> { chart_unix -> {O,H,L,C} } }
    """
    result: dict[str, dict[int, dict]] = {}
    if not log_path.exists():
        return result

    # Ligne exemple :
    #   M1 | 2026-04-01 00:00:00 |   1775289600 |    0.96047 |    0.96047 |    0.96033 |    0.96038 | DN
    pattern = re.compile(
        r"^\s*(\w+)\s*\|\s*([\d\- :]+)\s*\|\s*(\d+)\s*\|"
        r"\s*([\d.]+)\s*\|\s*([\d.]+)\s*\|\s*([\d.]+)\s*\|\s*([\d.]+)\s*\|\s*(\w+)"
    )
    with open(log_path, encoding="utf-8") as f:
        for line in f:
            m = pattern.match(line)
            if not m:
                continue
            tf, _qc, ch_unix, o, h, l, c, _dir = m.groups()
            tf = tf.strip()
            if tf not in TF_CODE:
                continue
            if tf not in result:
                result[tf] = {}
            result[tf][int(ch_unix)] = {
                "O": round(float(o), 5),
                "H": round(float(h), 5),
                "L": round(float(l), 5),
                "C": round(float(c), 5),
            }
    return result


def main():
    symbol   = sys.argv[1] if len(sys.argv) > 1 else "AUDCAD"
    from_str = sys.argv[2] if len(sys.argv) > 2 else "2026-04-01"
    to_str   = sys.argv[3] if len(sys.argv) > 3 else "2026-04-15"

    print(f"=== debug_full_compare.py ===")
    print(f"  Symbole : {symbol}")
    print(f"  Période : {from_str}  →  {to_str}")
    print()

    from_dt = datetime.strptime(from_str, "%Y-%m-%d").replace(tzinfo=timezone.utc)
    to_dt   = datetime.strptime(to_str,   "%Y-%m-%d").replace(
        hour=23, minute=59, second=59, tzinfo=timezone.utc)

    # ── MT5 ───────────────────────────────────────────────────────────────────
    print("Connexion MT5...")
    if not mt5.initialize():
        print("ERREUR : MT5 non disponible."); sys.exit(1)
    print("  MT5 OK")

    # ── DuckDB ────────────────────────────────────────────────────────────────
    script_dir = Path(__file__).resolve().parent
    db_path = None
    for parent in [script_dir, *script_dir.parents]:
        candidate = parent / "data" / "candles.db"
        if candidate.exists():
            db_path = candidate
            break
    if db_path is None:
        print("ERREUR : candles.db introuvable."); mt5.shutdown(); sys.exit(1)

    print(f"  DuckDB : {db_path}")
    con = duckdb.connect(str(db_path), read_only=True)

    # ── chart_candles.log ────────────────────────────────────────────────────
    log_dir        = db_path.parent.parent / "logs"
    chart_log_path = log_dir / "chart_candles.log"
    out_path       = log_dir / "full_compare.log"

    print(f"  Chart log : {chart_log_path}")
    if not chart_log_path.exists():
        print()
        print("  ATTENTION : chart_candles.log n'existe pas encore.")
        print("  Lance un replay dans l'app (Start) AVANT ce script.")
        print()

    print(f"  Output  : {out_path}")
    print()

    chart_data = parse_chart_log(chart_log_path)

    with open(out_path, "w", encoding="utf-8") as f:

        def w(line=""):
            f.write(line + "\n")
            print(line)

        w(f"=== Full Comparison : {symbol}  |  {from_str} → {to_str} ===")
        w(f"Généré le : {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        w("Colonnes : UTC | QC | MT5-OHLC | DB-OHLC | CHART-OHLC | Statut")
        w("=" * 160)

        grand_ok = grand_mismatch = grand_chart_missing = grand_data_missing = 0

        for tf_name, tf_code in TF_CODE.items():
            w()
            w("=" * 160)
            w(f"  TIMEFRAME : {tf_name}")
            w("=" * 160)

            # MT5
            rates = mt5.copy_rates_range(symbol, tf_code, from_dt, to_dt)
            mt5_map = {}
            if rates is not None:
                for r in rates:
                    mt5_map[int(r["time"])] = {
                        "O": round(float(r["open"]),  5),
                        "H": round(float(r["high"]),  5),
                        "L": round(float(r["low"]),   5),
                        "C": round(float(r["close"]), 5),
                    }

            # DuckDB
            rows = con.execute("""
                SELECT time, open, high, low, close FROM candles
                WHERE symbol=? AND timeframe=? AND time>=? AND time<=?
                ORDER BY time
            """, [symbol, tf_name, from_dt, to_dt]).fetchall()
            db_map = {}
            for row in rows:
                t = row[0]
                k = int(t.timestamp()) if hasattr(t, "timestamp") else int(t)
                db_map[k] = {
                    "O": round(float(row[1]), 5), "H": round(float(row[2]), 5),
                    "L": round(float(row[3]), 5), "C": round(float(row[4]), 5),
                }

            # Chart (keyed by chart_unix → convert back via inverse for alignment)
            # chart_unix = to_chart_unix(utc_ts)  →  chart_unix_map: chart_unix → OHLC
            ch_by_chart_unix = chart_data.get(tf_name, {})

            tf_ok = tf_mm = tf_cm = tf_dm = 0

            HDR = (f"{'UTC':>22}  {'QC':>14}  "
                   f"{'MT5-O':>9} {'MT5-C':>9}  "
                   f"{'DB-O':>9} {'DB-C':>9}  "
                   f"{'CH-O':>9} {'CH-C':>9}  Statut")
            w(HDR)
            w("-" * 160)

            all_utc_keys = sorted(set(list(mt5_map.keys()) + list(db_map.keys())))

            for k in all_utc_keys:
                utc_str = datetime.fromtimestamp(k, tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
                qc_str  = datetime.fromtimestamp(k, tz=QUEBEC_TZ).strftime("%m-%d %H:%M")
                ch_ts   = to_chart_unix(k)

                m  = mt5_map.get(k)
                d  = db_map.get(k)
                ch = ch_by_chart_unix.get(ch_ts)

                m_o  = f"{m['O']:.5f}"  if m  else "  n/a  "
                m_c  = f"{m['C']:.5f}"  if m  else "  n/a  "
                d_o  = f"{d['O']:.5f}"  if d  else "  n/a  "
                d_c  = f"{d['C']:.5f}"  if d  else "  n/a  "
                ch_o = f"{ch['O']:.5f}" if ch else "  n/a  "
                ch_c = f"{ch['C']:.5f}" if ch else "  n/a  "

                # Statut
                data_ok  = (m and d and m["O"] == d["O"] and m["C"] == d["C"]
                                     and m["H"] == d["H"] and m["L"] == d["L"])
                chart_ok = (d and ch and d["O"] == ch["O"] and d["C"] == ch["C"]
                                      and d["H"] == ch["H"] and d["L"] == ch["L"])

                if not m and not d:
                    status = "SKIP"
                elif not ch:
                    status = "CHART-MISSING"
                    tf_cm += 1
                elif not data_ok:
                    status = "*** DATA-MISMATCH ***"
                    tf_mm += 1
                elif not chart_ok:
                    status = "*** CHART-MISMATCH ***"
                    tf_mm += 1
                else:
                    status = "OK"
                    tf_ok += 1

                line = (f"{utc_str:>22}  {qc_str:>14}  "
                        f"{m_o:>9} {m_c:>9}  "
                        f"{d_o:>9} {d_c:>9}  "
                        f"{ch_o:>9} {ch_c:>9}  {status}")

                # Vers fichier toujours ; vers console seulement si pas OK
                f.write(line + "\n")
                if status != "OK":
                    print(line)

            grand_ok           += tf_ok
            grand_mismatch     += tf_mm
            grand_chart_missing += tf_cm

            w(f"\n  {tf_name} : OK={tf_ok} | MISMATCH={tf_mm} | CHART-MISSING={tf_cm}")

        w()
        w("=" * 160)
        w("  RÉSUMÉ GLOBAL")
        w("=" * 160)
        w(f"  OK             : {grand_ok}")
        w(f"  MISMATCH       : {grand_mismatch}  {'<-- PROBLÈME DONNÉES' if grand_mismatch else ''}")
        w(f"  CHART-MISSING  : {grand_chart_missing}  "
          f"{'(bougies hors range replay — normal)' if grand_chart_missing else ''}")

        if grand_mismatch == 0 and grand_ok > 0:
            w()
            w("  CONCLUSION : Données 100% identiques MT5=DuckDB=Chart.")
            w("  Le problème visuel est dans le RENDU TradingView, pas dans les données.")

    con.close()
    mt5.shutdown()
    print()
    print(f"Rapport complet : {out_path}")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        input("\nAppuie sur Entrée pour fermer...")
        sys.exit(1)
