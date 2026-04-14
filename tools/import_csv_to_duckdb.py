"""
ToutieTrader — Import CSV → DuckDB #1 (candles historiques)
Script one-shot. Lit tous les CSV dans ToutieTrader/artifacts/market_data/forex/
et les insère dans ToutieTrading/data/candles.db (DuckDB #1, lecture seule pour le Replay).

Usage :
    python tools/import_csv_to_duckdb.py

Dépendances :
    pip install duckdb pandas pytz
"""

import os
import sys
import time
from pathlib import Path

import duckdb
import pandas as pd
import pytz

# ─── Chemins ──────────────────────────────────────────────────────────────────

CSV_DIR  = Path(r"C:\Users\XBurnsX\ToutieTrader\artifacts\market_data\forex")
DB_PATH  = Path(__file__).resolve().parent.parent / "data" / "candles.db"

QUEBEC_TZ = pytz.timezone("America/Toronto")

VALID_TF  = {"M1", "M5", "M15", "H1", "H4", "D"}

# ─── Helpers ──────────────────────────────────────────────────────────────────

def parse_filename(stem: str) -> tuple[str, str] | None:
    """
    'AUD_CAD_M15' → ('AUD_CAD', 'M15')
    Retourne None si le TF n'est pas reconnu.
    """
    parts = stem.split("_")
    tf = parts[-1]
    if tf not in VALID_TF:
        return None
    symbol = "_".join(parts[:-1])
    return symbol, tf


def ms_to_quebec_ts(ms_series: pd.Series) -> pd.Series:
    """
    Convertit une série de timestamps Unix (millisecondes, UTC)
    en DatetimeIndex offset-aware heure Québec (America/Toronto).
    """
    utc = pd.to_datetime(ms_series, unit="ms", utc=True)
    return utc.dt.tz_convert(QUEBEC_TZ)


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    if not CSV_DIR.exists():
        print(f"[ERREUR] Dossier CSV introuvable : {CSV_DIR}")
        sys.exit(1)

    csv_files = sorted(CSV_DIR.glob("*.csv"))
    if not csv_files:
        print(f"[ERREUR] Aucun CSV trouvé dans : {CSV_DIR}")
        sys.exit(1)

    print(f"[INFO] {len(csv_files)} fichiers CSV trouvés dans {CSV_DIR}")
    print(f"[INFO] Destination : {DB_PATH}")

    DB_PATH.parent.mkdir(parents=True, exist_ok=True)

    if DB_PATH.exists():
        answer = input(f"\n[ATTENTION] {DB_PATH} existe déjà. Écraser ? (oui/non) : ").strip().lower()
        if answer != "oui":
            print("[ANNULÉ] Aucune modification.")
            sys.exit(0)
        DB_PATH.unlink()
        print("[INFO] Ancienne DB supprimée.")

    con = duckdb.connect(str(DB_PATH))

    # Création table + index
    con.execute("""
        CREATE TABLE candles (
            symbol    VARCHAR     NOT NULL,
            timeframe VARCHAR     NOT NULL,
            time      TIMESTAMPTZ NOT NULL,
            open      DOUBLE      NOT NULL,
            high      DOUBLE      NOT NULL,
            low       DOUBLE      NOT NULL,
            close     DOUBLE      NOT NULL,
            volume    BIGINT      NOT NULL,
            PRIMARY KEY (symbol, timeframe, time)
        )
    """)
    con.execute("CREATE INDEX idx_candles ON candles (symbol, timeframe, time)")

    print()
    total_rows = 0
    skipped    = 0
    t_start    = time.perf_counter()

    for csv_path in csv_files:
        parsed = parse_filename(csv_path.stem)
        if parsed is None:
            print(f"  [SKIP] Nom non reconnu : {csv_path.name}")
            skipped += 1
            continue

        symbol, tf = parsed

        try:
            df = pd.read_csv(csv_path, encoding="utf-8-sig")
        except Exception as e:
            print(f"  [ERREUR] Lecture {csv_path.name} : {e}")
            skipped += 1
            continue

        # Validation colonnes minimales
        required = {"ts", "open", "high", "low", "close", "volume"}
        if not required.issubset(df.columns):
            print(f"  [ERREUR] Colonnes manquantes dans {csv_path.name} : {required - set(df.columns)}")
            skipped += 1
            continue

        if df.empty:
            print(f"  [SKIP] Fichier vide : {csv_path.name}")
            skipped += 1
            continue

        # Conversion timestamp UTC ms → heure Québec offset-aware
        df["time"] = ms_to_quebec_ts(df["ts"])

        insert_df = pd.DataFrame({
            "symbol":    symbol,
            "timeframe": tf,
            "time":      df["time"],
            "open":      df["open"].round(5),
            "high":      df["high"].round(5),
            "low":       df["low"].round(5),
            "close":     df["close"].round(5),
            "volume":    df["volume"].astype("int64"),
        })

        n = len(insert_df)

        try:
            con.execute("INSERT INTO candles SELECT * FROM insert_df")
        except Exception as e:
            print(f"  [ERREUR] Insert {csv_path.name} : {e}")
            skipped += 1
            continue

        total_rows += n
        print(f"  [OK] {csv_path.name:<30}  {symbol:<12}  {tf:<4}  {n:>8,} bougies")

    # ─── Rapport final ────────────────────────────────────────────────────────

    elapsed = time.perf_counter() - t_start

    print()
    print("=" * 60)
    print(f"  Fichiers traités : {len(csv_files) - skipped} / {len(csv_files)}")
    print(f"  Fichiers ignorés : {skipped}")
    print(f"  Total bougies    : {total_rows:,}")
    print(f"  Durée            : {elapsed:.1f}s")

    con.close()
    db_size_mb = DB_PATH.stat().st_size / (1024 * 1024)
    print(f"  Taille DB        : {db_size_mb:.1f} MB")
    print(f"  Chemin DB        : {DB_PATH}")
    print("=" * 60)

    # Vérification rapide : count par paire/TF
    print("\n[VALIDATION] Bougies par symbole/TF :\n")
    con = duckdb.connect(str(DB_PATH), read_only=True)
    result = con.execute("""
        SELECT symbol, timeframe, COUNT(*) AS n
        FROM candles
        GROUP BY symbol, timeframe
        ORDER BY symbol, timeframe
    """).fetchall()
    con.close()

    for row in result:
        print(f"  {row[0]:<15}  {row[1]:<4}  {row[2]:>8,} bougies")

    print("\n[DONE] Import terminé.")


if __name__ == "__main__":
    main()
