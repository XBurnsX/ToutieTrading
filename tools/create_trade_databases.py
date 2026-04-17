"""
ToutieTrader — Création DuckDB #2 (trades live) + DB Replay (trades temporaires)
Script one-shot pour la fondation. Les deux DB utilisent le même schéma exact.

- data/trades.db       → DuckDB #2, persistant, trades live/démo
- data/replay_trades.db → DB Replay, wiped à chaque fermeture app / Reset Replay

Usage :
    python tools/create_trade_databases.py

Dépendances :
    pip install duckdb
"""

import sys
import duckdb
from pathlib import Path

# ─── Chemins ──────────────────────────────────────────────────────────────────

DATA_DIR   = Path(__file__).resolve().parent.parent / "data"
TRADES_DB  = DATA_DIR / "trades.db"
REPLAY_DB  = DATA_DIR / "replay_trades.db"

# ─── Schéma ───────────────────────────────────────────────────────────────────

CREATE_TRADES_TABLE = """
    CREATE TABLE IF NOT EXISTS trades (
        id                UUID        PRIMARY KEY,
        symbol            VARCHAR     NOT NULL,
        strategy_name     VARCHAR     NOT NULL,
        strategy_settings JSON        NOT NULL,
        direction         VARCHAR     NOT NULL,        -- 'BUY' | 'SELL'
        entry_time        TIMESTAMPTZ,
        entry_price       DOUBLE,
        sl                DOUBLE,
        tp                DOUBLE,
        exit_time         TIMESTAMPTZ,
        exit_price        DOUBLE,
        profit_loss       DOUBLE,
        risk_dollars      DOUBLE,
        lot_size          DOUBLE,
        ticket_id         BIGINT,
        correlation_id    UUID        NOT NULL,
        exit_reason       VARCHAR,                     -- 'TP' | 'SL' | 'ForceExit:[label]' | 'OptionalExit:[label]'
        conditions_met    JSON,                        -- JSON array des labels de conditions remplies
        error_log         VARCHAR,                     -- NULL si succès, message si erreur
        fees              DOUBLE                       -- Frais totaux $ (spread + commission round-trip)
    )
"""

CREATE_INDEXES = [
    "CREATE INDEX IF NOT EXISTS idx_trades_symbol        ON trades (symbol)",
    "CREATE INDEX IF NOT EXISTS idx_trades_entry_time    ON trades (entry_time)",
    "CREATE INDEX IF NOT EXISTS idx_trades_strategy_name ON trades (strategy_name)",
]

# ─── Helper ───────────────────────────────────────────────────────────────────

def setup_db(db_path: Path, label: str, overwrite: bool = False) -> bool:
    if db_path.exists():
        if overwrite:
            db_path.unlink()
            print(f"  [INFO] Ancienne {label} supprimée.")
        else:
            print(f"  [SKIP] {label} existe déjà : {db_path}")
            return False

    con = duckdb.connect(str(db_path))
    con.execute(CREATE_TRADES_TABLE)
    for idx_sql in CREATE_INDEXES:
        con.execute(idx_sql)
    con.close()

    size_kb = db_path.stat().st_size / 1024
    print(f"  [OK]   {label} créée : {db_path}  ({size_kb:.1f} KB)")
    return True

# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    DATA_DIR.mkdir(parents=True, exist_ok=True)

    print("=" * 60)
    print("  ToutieTrader — Setup bases de données trades")
    print("=" * 60)
    print()

    # DuckDB #2 — trades live (ne jamais écraser sans confirmation)
    overwrite_trades = False
    if TRADES_DB.exists():
        answer = input(f"[ATTENTION] trades.db existe déjà. Écraser ? (oui/non) : ").strip().lower()
        overwrite_trades = (answer == "oui")

    setup_db(TRADES_DB,  "DuckDB #2 (trades live)", overwrite=overwrite_trades)

    print()

    # DB Replay — toujours recréée (wipée à chaque session de toute façon)
    setup_db(REPLAY_DB, "DB Replay (trades temporaires)", overwrite=True)

    print()
    print("=" * 60)

    # Validation : vérifier que les tables existent et sont vides
    print("\n[VALIDATION]\n")
    for db_path, label in [(TRADES_DB, "trades.db"), (REPLAY_DB, "replay_trades.db")]:
        if not db_path.exists():
            continue
        con = duckdb.connect(str(db_path), read_only=True)
        count = con.execute("SELECT COUNT(*) FROM trades").fetchone()[0]
        cols  = [row[0] for row in con.execute(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'trades' ORDER BY ordinal_position"
        ).fetchall()]
        con.close()
        print(f"  {label}")
        print(f"    Colonnes ({len(cols)}) : {', '.join(cols)}")
        print(f"    Lignes   : {count}")
        print()

    print("[DONE] Setup terminé.")


if __name__ == "__main__":
    main()
