"""
Wrapper MT5 — seul fichier qui touche MetaTrader5.
Toutes les timestamps retournées sont déjà converties en heure Québec offset-aware.
"""

from datetime import datetime, timedelta, timezone
from pathlib import Path
import time
from typing import Optional

import MetaTrader5 as mt5
import duckdb
import pandas as pd

from time_utils import unix_to_quebec, utc_now_quebec, iso_to_utc_naive, format_iso, QUEBEC_TZ
import correlation


# ─── Timeframes ───────────────────────────────────────────────────────────────

_TF_MAP: dict[str, int] = {
    "M1":  mt5.TIMEFRAME_M1,
    "M5":  mt5.TIMEFRAME_M5,
    "M15": mt5.TIMEFRAME_M15,
    "H1":  mt5.TIMEFRAME_H1,
    "H4":  mt5.TIMEFRAME_H4,
    "D":   mt5.TIMEFRAME_D1,
}

BOT_TAG = "ToutieTrader"


# ─── Init / Shutdown ──────────────────────────────────────────────────────────

def initialize() -> bool:
    return mt5.initialize()


def shutdown() -> None:
    mt5.shutdown()


def is_connected() -> bool:
    """True si MT5 est initialisé et le terminal répond."""
    try:
        info = mt5.terminal_info()
        return info is not None and info.connected
    except Exception:
        return False


# ─── /status ──────────────────────────────────────────────────────────────────

def get_status() -> dict:
    return {
        "python": True,
        "mt5": is_connected(),
    }


# ─── /account ─────────────────────────────────────────────────────────────────

def get_account() -> dict:
    """
    Retourne balance, equity, drawdown_percent, currency.
    Lève RuntimeError si MT5 unavailable.
    """
    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    info = mt5.account_info()
    if info is None:
        raise RuntimeError("MT5 unavailable")

    balance = round(info.balance, 2)
    equity  = round(info.equity, 2)
    drawdown = round((balance - equity) / balance * 100, 2) if balance > 0 else 0.0

    return {
        "balance":          balance,
        "equity":           equity,
        "drawdown_percent": drawdown,
        "currency":         info.currency,
    }


# ─── /candles ─────────────────────────────────────────────────────────────────

def get_candles(
    symbol: str,
    timeframe: str,
    count: Optional[int] = None,
    from_iso: Optional[str] = None,
    to_iso: Optional[str] = None,
) -> list[dict]:
    """
    Retourne les bougies converties en heure Québec offset-aware.
    Lève ValueError si paramètres invalides, RuntimeError si MT5 unavailable.
    """
    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    tf = _TF_MAP.get(timeframe)
    if tf is None:
        raise ValueError(f"Timeframe invalide : {timeframe}. Valides : {list(_TF_MAP)}")

    if count is not None:
        rates = mt5.copy_rates_from_pos(symbol, tf, 0, count)
    elif from_iso and to_iso:
        dt_from = iso_to_utc_naive(from_iso)
        dt_to   = iso_to_utc_naive(to_iso)
        rates   = mt5.copy_rates_range(symbol, tf, dt_from, dt_to)
    else:
        raise ValueError("Fournir 'count' ou 'from'+'to'.")

    if rates is None or len(rates) == 0:
        return []

    result = []
    for r in rates:
        dt_qc = unix_to_quebec(int(r["time"]))
        result.append({
            "time":   format_iso(dt_qc),
            "open":   round(float(r["open"]),  5),
            "high":   round(float(r["high"]),  5),
            "low":    round(float(r["low"]),   5),
            "close":  round(float(r["close"]), 5),
            "volume": int(r["tick_volume"]),
        })
    return result


# ─── /order ───────────────────────────────────────────────────────────────────

def send_order(
    correlation_id: str,
    symbol: str,
    direction: str,
    lot_size: float,
    sl: float,
    tp: float,
) -> dict:
    """
    Envoie un market order via MT5.
    - Anti double-envoi : si correlation_id déjà vu → retourne la réponse originale.
    - Lève RuntimeError si MT5 unavailable.
    - Lève ValueError si ordre rejeté ou marché fermé.
    """
    # Anti double-envoi
    cached = correlation.get(correlation_id)
    if cached:
        return cached

    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    order_type = mt5.ORDER_TYPE_BUY if direction == "BUY" else mt5.ORDER_TYPE_SELL

    request = {
        "action":       mt5.TRADE_ACTION_DEAL,
        "symbol":       symbol,
        "volume":       round(lot_size, 2),
        "type":         order_type,
        "sl":           sl,
        "tp":           tp,
        "type_time":    mt5.ORDER_TIME_GTC,
        "type_filling": mt5.ORDER_FILLING_IOC,
        "comment":      f"{BOT_TAG}:{correlation_id[:8]}",
    }

    result = mt5.order_send(request)

    if result is None:
        raise ValueError("Ordre rejeté : aucune réponse MT5")

    if result.retcode != mt5.TRADE_RETCODE_DONE:
        comment_lower = result.comment.lower()
        if "market closed" in comment_lower or "trade is disabled" in comment_lower:
            raise MarketClosedError()
        raise ValueError(result.comment or f"Code erreur MT5 : {result.retcode}")

    fill_time = format_iso(utc_now_quebec())

    response = {
        "ticket":     result.order,
        "fill_price": round(result.price, 5),
        "time":       fill_time,
    }

    correlation.save(correlation_id, result.order, round(result.price, 5), fill_time)
    return response


# ─── /close_order ─────────────────────────────────────────────────────────────

def close_order(ticket: int) -> dict:
    """
    Ferme une position ouverte par son ticket.
    Lève TicketNotFoundError si introuvable, RuntimeError si MT5 unavailable.
    """
    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    positions = mt5.positions_get(ticket=ticket)
    if not positions:
        raise TicketNotFoundError(ticket)

    pos = positions[0]
    close_type = mt5.ORDER_TYPE_SELL if pos.type == mt5.ORDER_TYPE_BUY else mt5.ORDER_TYPE_BUY

    request = {
        "action":       mt5.TRADE_ACTION_DEAL,
        "position":     ticket,
        "symbol":       pos.symbol,
        "volume":       pos.volume,
        "type":         close_type,
        "type_time":    mt5.ORDER_TIME_GTC,
        "type_filling": mt5.ORDER_FILLING_IOC,
        "comment":      f"{BOT_TAG}:close",
    }

    result = mt5.order_send(request)

    if result is None or result.retcode != mt5.TRADE_RETCODE_DONE:
        reason = result.comment if result else "Aucune réponse MT5"
        raise ValueError(reason)

    close_time = format_iso(utc_now_quebec())

    return {
        "closed":      True,
        "close_price": round(result.price, 5),
        "time":        close_time,
    }


# ─── /ensure_candles_range ────────────────────────────────────────────────────
#
# Lazy fetch : au Start du Replay, le C# appelle ce endpoint pour garantir que
# toutes les bougies de la watchlist MT5 × tous les TFs sont présentes dans le
# candles.db pour la range demandée. On télécharge SEULEMENT ce qui manque.
# Première run d'une range = ~30-60s. Runs suivants de la même range = instant.

# Chemin vers la DB #1 (candles historiques) — résolu dynamiquement au 1er appel
_CANDLES_DB_PATH: Optional[Path] = None


def _resolve_db_path() -> Path:
    """
    Localise candles.db : remonte depuis python_bridge/ jusqu'à trouver data/candles.db.
    """
    global _CANDLES_DB_PATH
    if _CANDLES_DB_PATH is not None:
        return _CANDLES_DB_PATH

    here = Path(__file__).resolve().parent
    for parent in [here, *here.parents]:
        candidate = parent / "data" / "candles.db"
        if candidate.exists() or (parent / "data").exists():
            candidate.parent.mkdir(parents=True, exist_ok=True)
            _CANDLES_DB_PATH = candidate
            return candidate
    # Fallback : relatif au script
    fallback = here.parent / "data" / "candles.db"
    fallback.parent.mkdir(parents=True, exist_ok=True)
    _CANDLES_DB_PATH = fallback
    return fallback


# Suffixes broker courants à stripper pour obtenir un nom canonique
_BROKER_SUFFIXES = [
    ".pro", ".raw", ".cash", ".ecn", ".stp", ".m", ".r", ".i", ".c", ".s", ".e",
    "Cash", "cash", "Pro", "pro", "ECN", "ecn",
]


def _mt5_to_canonical(mt5_name: str) -> str:
    """
    Convertit un nom MT5 broker-natif en nom canonique pour stockage DB.
    Ex: 'EURUSD.m' → 'EURUSD', 'US30.cash' → 'US30', 'GOLDm' → 'GOLD'
    """
    name = mt5_name
    # Strip suffixes avec point
    for suffix in _BROKER_SUFFIXES:
        if name.endswith(suffix):
            name = name[:-len(suffix)]
            break
    # Strip trailing single-char modifiers (m, r, i, +, -)
    while name and name[-1] in "mri+-":
        # Seulement si le char précédent est une lettre majuscule (évite de manger "USDm" → "USD" mais laisse "EURUSD" intact)
        if len(name) > 1 and name[-2].isupper():
            name = name[:-1]
        else:
            break
    # Strip prefix # ou .
    name = name.lstrip("#.")
    return name


def _tf_seconds(tf_name: str) -> int:
    return {
        "M1":   60, "M5":  300, "M15":  900, "M30": 1800,
        "H1": 3600, "H4": 14400, "D":  86400,
    }.get(tf_name, 3600)


def _get_watchlist_symbols() -> list:
    """
    Retourne la liste des symbols dans le Market Watch MT5 (ceux visibles = watchlist).
    """
    all_symbols = mt5.symbols_get()
    if all_symbols is None:
        return []
    return [s for s in all_symbols if getattr(s, "visible", False)]


def get_watchlist() -> list[dict]:
    """
    Retourne la watchlist MT5 avec noms broker + canoniques.
    Appelé par le C# au démarrage pour peupler le dropdown symbole sans toucher la DB.
    Lève RuntimeError si MT5 unavailable.
    """
    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    watchlist = _get_watchlist_symbols()
    return [
        {
            "mt5_name":       s.name,
            "canonical_name": _mt5_to_canonical(s.name),
        }
        for s in watchlist
    ]


def _fetch_mt5_range(mt5_sym: str, tf_code: int, from_dt: datetime, to_dt: datetime, tf_name: str):
    """
    Télécharge les bougies en chunks adaptés au TF.
    Retry 3× par chunk si vide (MT5 peut encore sync avec le serveur).
    Retourne un numpy array ou None.
    """
    chunk_days = 90 if tf_name in ("M1", "M5") else 365
    cursor = from_dt
    all_rates = []

    while cursor < to_dt:
        chunk_end = min(cursor + timedelta(days=chunk_days), to_dt)

        rates = None
        for attempt in range(3):
            rates = mt5.copy_rates_range(mt5_sym, tf_code, cursor, chunk_end)
            if rates is not None and len(rates) > 0:
                break
            time.sleep(0.5 * (attempt + 1))

        if rates is not None and len(rates) > 0:
            all_rates.extend(rates.tolist())

        cursor = chunk_end

    return all_rates


def _warmup_mt5_symbol(mt5_sym: str, tf_code: int, tf_name: str, years_needed: float) -> None:
    """
    Force MT5 à télécharger l'historique depuis le serveur broker.
    Appelle copy_rates_from_pos() qui bloque jusqu'à ce que le download finisse.
    Utilise years_needed réel (pas de min 0.5) → 10× plus rapide pour ranges courtes.
    """
    bars_per_year = {
        "M1": 525_600, "M5": 105_120, "M15": 35_040, "M30": 17_520,
        "H1": 8_760, "H4": 2_190, "D": 260,
    }
    # +30% buffer + 100 barres warmup indicateurs — PAS de max(0.5) inutile
    target_bars = int(bars_per_year.get(tf_name, 10_000) * years_needed * 1.3) + 100
    target_bars = max(target_bars, 500)          # minimum 500 barres
    target_bars = min(target_bars, 2_000_000)

    for attempt in range(5):
        rates = mt5.copy_rates_from_pos(mt5_sym, tf_code, 0, target_bars)
        if rates is not None and len(rates) > 100:
            return
        time.sleep(1.5 * (attempt + 1))


def ensure_candles_range(
    from_iso: str,
    to_iso: str,
    timeframes: list[str],
) -> dict:
    """
    Endpoint principal lazy fetch MT5 → DuckDB.

    Pour chaque symbol dans la watchlist MT5 × chaque TF demandé :
      1. Query DuckDB pour min/max existants
      2. Calcule les ranges manquantes (avant min, après max)
      3. Pour chaque range manquante, fetch MT5 + INSERT DB
      4. Retourne un rapport détaillé

    Lève RuntimeError si MT5 unavailable.
    """
    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    # Parse dates
    # NAIVE UTC pour les appels MT5 (mt5.copy_rates_range attend naive ou aware)
    # AWARE UTC pour les queries DuckDB (éviter l'interprétation local-time sur TIMESTAMPTZ)
    dt_from_req = iso_to_utc_naive(from_iso)
    dt_to_req   = iso_to_utc_naive(to_iso)
    dt_from_aware = dt_from_req.replace(tzinfo=timezone.utc)
    dt_to_aware   = dt_to_req.replace(tzinfo=timezone.utc)

    # Validation TFs
    unknown_tfs = [tf for tf in timeframes if tf not in _TF_MAP]
    if unknown_tfs:
        raise ValueError(f"Timeframes inconnues : {unknown_tfs}. Valides : {list(_TF_MAP)}")

    # Watchlist MT5
    watchlist = _get_watchlist_symbols()
    if not watchlist:
        raise RuntimeError("Watchlist MT5 vide — aucun symbole à importer")

    # DuckDB — connexion en écriture, fermée dans le finally pour éviter les locks
    db_path = _resolve_db_path()
    con = duckdb.connect(str(db_path))
    try:

        # Schéma (idempotent)
        con.execute("""
            CREATE TABLE IF NOT EXISTS candles (
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

        t_start        = time.perf_counter()
        total_inserted = 0
        total_cached   = 0
        reports        = []

        # Estimation du nb d'années couvertes pour le warmup
        years_needed = max(0.1, (dt_to_req - dt_from_req).days / 365.0)

        def _to_utc_aware(x):
            """Normalise un datetime DuckDB TIMESTAMPTZ en aware UTC."""
            if x is None:
                return None
            if isinstance(x, datetime):
                return x.astimezone(timezone.utc) if x.tzinfo else x.replace(tzinfo=timezone.utc)
            return None

        for s in watchlist:
            mt5_sym   = s.name
            canonical = _mt5_to_canonical(mt5_sym)

            inserted_by_tf  = {}
            cached_by_tf    = {}
            errors_by_tf    = {}
            symbol_selected = False   # lazy — seulement si download requis

            for tf_name in timeframes:
                tf_code      = _TF_MAP[tf_name]
                tf_sec       = _tf_seconds(tf_name)
                warmup_delta = timedelta(seconds=100 * tf_sec)

                dt_from_ext_naive = dt_from_req   - warmup_delta   # naive UTC (MT5)
                dt_to_ext_naive   = dt_to_req                       # naive UTC (MT5)
                dt_from_ext_aware = dt_from_aware - warmup_delta   # aware UTC (DuckDB)
                dt_to_ext_aware   = dt_to_aware                     # aware UTC (DuckDB)

                # ── 1. Check cache DuckDB EN PREMIER — évite le warmup si déjà présent ──
                row = con.execute(
                    "SELECT MIN(time), MAX(time) FROM candles WHERE symbol = ? AND timeframe = ?",
                    [canonical, tf_name],
                ).fetchone()
                db_min, db_max   = row if row else (None, None)
                db_min_aware     = _to_utc_aware(db_min)
                db_max_aware     = _to_utc_aware(db_max)

                missing_ranges_naive = []
                missing_ranges_aware = []
                if db_min_aware is None:
                    missing_ranges_naive.append((dt_from_ext_naive, dt_to_ext_naive))
                    missing_ranges_aware.append((dt_from_ext_aware, dt_to_ext_aware))
                else:
                    if dt_from_ext_aware < db_min_aware:
                        db_min_naive = db_min_aware.astimezone(timezone.utc).replace(tzinfo=None)
                        missing_ranges_naive.append((dt_from_ext_naive, db_min_naive))
                        missing_ranges_aware.append((dt_from_ext_aware, db_min_aware))
                    if dt_to_ext_aware > db_max_aware:
                        db_max_naive = db_max_aware.astimezone(timezone.utc).replace(tzinfo=None)
                        missing_ranges_naive.append((db_max_naive, dt_to_ext_naive))
                        missing_ranges_aware.append((db_max_aware, dt_to_ext_aware))

                if not missing_ranges_naive:
                    # Entièrement en cache — zero appel MT5
                    existing = con.execute(
                        "SELECT COUNT(*) FROM candles WHERE symbol=? AND timeframe=? AND time >= ? AND time <= ?",
                        [canonical, tf_name, dt_from_ext_aware, dt_to_ext_aware],
                    ).fetchone()[0]
                    cached_by_tf[tf_name] = int(existing)
                    continue

                # ── 2. Download requis — symbol_select + warmup (lazy, une seule fois) ──
                if not symbol_selected:
                    if not mt5.symbol_select(mt5_sym, True):
                        for tf in timeframes:
                            if tf not in inserted_by_tf and tf not in cached_by_tf:
                                errors_by_tf[tf] = "symbol_select failed"
                        break
                    symbol_selected = True

                try:
                    _warmup_mt5_symbol(mt5_sym, tf_code, tf_name, years_needed)
                except Exception as e:
                    errors_by_tf[tf_name] = f"warmup: {e}"
                    continue

                # ── 3. Fetch les ranges manquantes depuis MT5 ──
                inserted_this_tf = 0
                for ((range_from_naive, range_to_naive), (range_from_aware, range_to_aware)) \
                        in zip(missing_ranges_naive, missing_ranges_aware):
                    try:
                        rates = _fetch_mt5_range(mt5_sym, tf_code, range_from_naive, range_to_naive, tf_name)
                    except Exception as e:
                        errors_by_tf[tf_name] = f"fetch: {e}"
                        continue

                    if not rates:
                        continue

                    df = pd.DataFrame(rates, columns=[
                        "time", "open", "high", "low", "close",
                        "tick_volume", "spread", "real_volume",
                    ])
                    df = df.drop_duplicates(subset=["time"]).sort_values("time")
                    df["time"] = pd.to_datetime(df["time"], unit="s", utc=True).dt.tz_convert(QUEBEC_TZ)

                    insert_df = pd.DataFrame({
                        "symbol":    canonical,
                        "timeframe": tf_name,
                        "time":      df["time"],
                        "open":      df["open"].round(5),
                        "high":      df["high"].round(5),
                        "low":       df["low"].round(5),
                        "close":     df["close"].round(5),
                        "volume":    df["tick_volume"].astype("int64"),
                    })

                    con.execute("INSERT OR REPLACE INTO candles SELECT * FROM insert_df")
                    inserted_this_tf += len(insert_df)

                inserted_by_tf[tf_name] = inserted_this_tf
                total_inserted         += inserted_this_tf

                cached_count = con.execute(
                    "SELECT COUNT(*) FROM candles WHERE symbol=? AND timeframe=? AND time >= ? AND time <= ?",
                    [canonical, tf_name, dt_from_ext_aware, dt_to_ext_aware],
                ).fetchone()[0]
                cached_by_tf[tf_name] = int(cached_count) - inserted_this_tf
                total_cached          += cached_by_tf[tf_name]

            reports.append({
                "mt5_name":       mt5_sym,
                "canonical_name": canonical,
                "inserted":       inserted_by_tf,
                "cached":         cached_by_tf,
                "errors":         errors_by_tf,
            })

        result = {
            "total_symbols":  len(watchlist),
            "total_inserted": total_inserted,
            "total_cached":   total_cached,
            "elapsed_sec":    round(time.perf_counter() - t_start, 2),
            "symbols":        reports,
        }

    finally:
        con.close()   # toujours fermé — même en cas d'exception

    return result


# ─── Exceptions personnalisées ────────────────────────────────────────────────

class MarketClosedError(Exception):
    pass


class TicketNotFoundError(Exception):
    def __init__(self, ticket: int):
        self.ticket = ticket
        super().__init__(f"Ticket {ticket} introuvable")
