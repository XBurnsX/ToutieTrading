"""
Wrapper MT5 — seul fichier qui touche MetaTrader5.
Toutes les timestamps retournées sont déjà converties en heure Québec offset-aware.
"""

from datetime import datetime, timedelta, timezone
from pathlib import Path
import math
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
BOT_MAGIC = 20260417
DEFAULT_DEVIATION = 20

# ─── Download progress (thread-safe via GIL — dict replacement is atomic) ────
_download_progress: dict = {"current": "", "index": 0, "total": 0}


def get_download_progress() -> dict:
    """Retourne l'état courant du téléchargement ensure_candles_range."""
    return _download_progress


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
    Retourne balance, equity, drawdown_percent, currency et marge.
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
        "profit":           round(float(getattr(info, "profit", 0.0) or 0.0), 2),
        "margin":           round(float(getattr(info, "margin", 0.0) or 0.0), 2),
        "free_margin":      round(float(getattr(info, "margin_free", 0.0) or 0.0), 2),
        "margin_level":     round(float(getattr(info, "margin_level", 0.0) or 0.0), 2),
        "login":            int(getattr(info, "login", 0) or 0),
        "server":           str(getattr(info, "server", "") or ""),
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

    mt5_name = _resolve_mt5_symbol(symbol)

    if count is not None:
        rates = mt5.copy_rates_from_pos(mt5_name, tf, 0, count)
    elif from_iso and to_iso:
        dt_from = iso_to_utc_naive(from_iso)
        dt_to   = iso_to_utc_naive(to_iso)
        rates   = mt5.copy_rates_range(mt5_name, tf, dt_from, dt_to)
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

    mt5_name = _resolve_mt5_symbol(symbol)
    info = mt5.symbol_info(mt5_name)
    if info is None:
        raise ValueError(f"Symbol not found: {symbol}")

    tick = mt5.symbol_info_tick(mt5_name)
    if tick is None:
        raise ValueError(f"No tick available for symbol: {mt5_name}")

    order_type = mt5.ORDER_TYPE_BUY if direction == "BUY" else mt5.ORDER_TYPE_SELL
    price = float(tick.ask) if order_type == mt5.ORDER_TYPE_BUY else float(tick.bid)
    if price <= 0:
        raise ValueError(f"No executable price available for symbol: {mt5_name}")

    volume = _normalize_volume(info, lot_size)
    comment = f"{BOT_TAG}:{correlation_id[:8]}"

    request = {
        "action":       mt5.TRADE_ACTION_DEAL,
        "symbol":       mt5_name,
        "volume":       volume,
        "type":         order_type,
        "price":        _round_price(price, info),
        "sl":           _round_price(sl, info),
        "tp":           _round_price(tp, info),
        "deviation":    DEFAULT_DEVIATION,
        "magic":        BOT_MAGIC,
        "type_time":    mt5.ORDER_TIME_GTC,
        "comment":      comment,
    }

    result = _send_order_with_filling(request)

    if result is None:
        raise ValueError("Ordre rejeté : aucune réponse MT5")

    if result.retcode != mt5.TRADE_RETCODE_DONE:
        comment_lower = result.comment.lower()
        if "market closed" in comment_lower or "trade is disabled" in comment_lower:
            raise MarketClosedError()
        raise ValueError(result.comment or f"Code erreur MT5 : {result.retcode}")

    fill_time = format_iso(utc_now_quebec())

    ticket = _find_position_ticket(mt5_name, comment, result.order)

    response = {
        "ticket":     ticket,
        "fill_price": round(result.price, 5),
        "time":       fill_time,
    }

    correlation.save(correlation_id, ticket, round(result.price, 5), fill_time)
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
    tick = mt5.symbol_info_tick(pos.symbol)
    close_price = None
    if tick is not None:
        close_price = float(tick.bid) if close_type == mt5.ORDER_TYPE_SELL else float(tick.ask)

    request = {
        "action":       mt5.TRADE_ACTION_DEAL,
        "position":     ticket,
        "symbol":       pos.symbol,
        "volume":       pos.volume,
        "type":         close_type,
        "deviation":    DEFAULT_DEVIATION,
        "magic":        BOT_MAGIC,
        "type_time":    mt5.ORDER_TIME_GTC,
        "comment":      f"{BOT_TAG}:close",
    }
    if close_price and close_price > 0:
        request["price"] = close_price

    result = _send_order_with_filling(request)

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
def modify_stop_loss(ticket: int, sl: float) -> dict:
    """
    Modifie seulement le SL d'une position ouverte.
    Le TP existant est preserve.
    """
    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    positions = mt5.positions_get(ticket=ticket)
    if not positions:
        raise TicketNotFoundError(ticket)

    pos = positions[0]
    info = mt5.symbol_info(pos.symbol)
    if info is None:
        raise ValueError(f"Symbol info unavailable: {pos.symbol}")

    request = {
        "action":   mt5.TRADE_ACTION_SLTP,
        "position": ticket,
        "symbol":   pos.symbol,
        "sl":       _round_price(sl, info),
        "tp":       _round_price(float(pos.tp), info) if float(pos.tp or 0) > 0 else 0.0,
        "magic":    BOT_MAGIC,
        "comment":  f"{BOT_TAG}:modify_sl",
    }

    result = mt5.order_send(request)
    if result is None or result.retcode != mt5.TRADE_RETCODE_DONE:
        reason = result.comment if result else "Aucune reponse MT5"
        raise ValueError(reason)

    return {
        "modified": True,
        "sl":       request["sl"],
        "time":     format_iso(utc_now_quebec()),
    }


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


def _ensure_symbol_visible(mt5_name: str):
    info = mt5.symbol_info(mt5_name)
    if info is None:
        return None
    if not info.visible:
        mt5.symbol_select(mt5_name, True)
        info = mt5.symbol_info(mt5_name)
    return info


def _resolve_mt5_symbol(symbol: str) -> str:
    """
    Accepte un symbole canonique (JP225) ou broker-natif (JP225.cash).
    Retourne toujours le nom exact que MT5 doit recevoir.
    """
    direct = _ensure_symbol_visible(symbol)
    if direct is not None:
        return symbol

    requested = symbol.upper()
    for s in (mt5.symbols_get() or []):
        if _mt5_to_canonical(s.name).upper() == requested:
            if _ensure_symbol_visible(s.name) is None:
                raise ValueError(f"Symbol not selectable: {s.name}")
            return s.name

    raise ValueError(f"Symbol not found: {symbol}")


def _round_price(value: float, info) -> float:
    digits = int(getattr(info, "digits", 5) or 5)
    return round(float(value), digits)


def _volume_decimals(step: float) -> int:
    text = f"{step:.8f}".rstrip("0").rstrip(".")
    if "." not in text:
        return 0
    return len(text.split(".", 1)[1])


def _normalize_volume(info, lot_size: float) -> float:
    step = float(getattr(info, "volume_step", 0) or 0)
    min_vol = float(getattr(info, "volume_min", 0) or 0)
    max_vol = float(getattr(info, "volume_max", 0) or 0)
    volume = float(lot_size)

    if step > 0:
        volume = math.floor((volume / step) + 1e-9) * step
        volume = round(volume, _volume_decimals(step))

    if min_vol > 0 and volume + 1e-9 < min_vol:
        raise ValueError(f"Lot size {lot_size} below broker minimum {min_vol}")
    if max_vol > 0 and volume - 1e-9 > max_vol:
        raise ValueError(f"Lot size {lot_size} above broker maximum {max_vol}")

    return volume


def _send_order_with_filling(request: dict):
    invalid_fill = getattr(mt5, "TRADE_RETCODE_INVALID_FILL", 10030)
    attempts = []

    for filling in (
        mt5.ORDER_FILLING_IOC,
        mt5.ORDER_FILLING_FOK,
        mt5.ORDER_FILLING_RETURN,
    ):
        current = dict(request)
        current["type_filling"] = filling
        result = mt5.order_send(current)
        if result is None:
            return None
        attempts.append(result)
        if result.retcode == mt5.TRADE_RETCODE_DONE:
            return result
        if result.retcode != invalid_fill:
            return result

    return attempts[-1] if attempts else None


def _find_position_ticket(mt5_name: str, comment: str, order_ticket: int) -> int:
    # Sur certains comptes, result.order n'est pas le ticket de position.
    # On relit la position par commentaire pour que /close_order vise le bon ticket.
    time.sleep(0.2)
    positions = mt5.positions_get(symbol=mt5_name) or []

    for pos in positions:
        if str(getattr(pos, "comment", "")) == comment:
            return int(pos.ticket)

    for pos in positions:
        identifier = int(getattr(pos, "identifier", 0) or 0)
        if identifier == int(order_ticket):
            return int(pos.ticket)

    return int(order_ticket)


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


def get_symbol_info(symbol: str) -> dict:
    """
    Retourne les métadonnées MT5 d'un symbole.
    Accepte le nom canonique (ex: "EURUSD") ou broker-natif (ex: "EURUSD.m").
    Lève RuntimeError si MT5 unavailable, ValueError si symbol introuvable.
    """
    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    # Tenter d'abord le nom direct, sinon résoudre via watchlist
    info = mt5.symbol_info(symbol)
    mt5_name = symbol

    if info is None:
        # Le symbole demandé est peut-être canonical → chercher le broker-natif équivalent
        for s in (mt5.symbols_get() or []):
            if _mt5_to_canonical(s.name) == symbol:
                mt5_name = s.name
                info = mt5.symbol_info(mt5_name)
                break

    if info is None:
        raise ValueError(f"Symbol not found: {symbol}")

    # S'assurer que le symbole est sélectionné dans le Market Watch (pour avoir bid/ask)
    if not info.visible:
        mt5.symbol_select(mt5_name, True)
        info = mt5.symbol_info(mt5_name)

    tick = mt5.symbol_info_tick(mt5_name)
    bid = float(tick.bid) if tick is not None else 0.0
    ask = float(tick.ask) if tick is not None else 0.0

    # Calcul authoritatif "money per point per lot" via mt5.order_calc_profit().
    # Source de vérité : ce que MT5 retournerait comme profit pour 1 lot bougé d'1 unité de prix.
    # Évite les bugs des tick_value foireux pour CFDs/indices (cross-currency, contract_size weird).
    # MT5 arrondit le profit à 2 décimales — on query avec un grand delta puis on normalise.
    money_per_point_per_lot = 0.0
    try:
        ref_price = ask if ask > 0 else bid
        if ref_price > 0 and info.point > 0 and info.volume_min > 0:
            # Delta = 1% du prix (pour avoir un profit non négligeable malgré l'arrondi MT5).
            delta = max(ref_price * 0.01, info.point * 1000)
            # Use volume_min pour s'assurer que MT5 accepte la volume.
            vol = info.volume_min
            p = mt5.order_calc_profit(mt5.ORDER_TYPE_BUY, mt5_name, vol, ref_price, ref_price + delta)
            if p is not None and p > 0 and delta > 0 and vol > 0:
                # Profit = priceDiff × moneyPerPointPerLot × lot
                # → moneyPerPointPerLot = profit / (delta × lot)
                money_per_point_per_lot = float(p) / (delta * vol)
    except Exception:
        pass

    return {
        "mt5_name":               mt5_name,
        "canonical_name":         _mt5_to_canonical(mt5_name),
        "digits":                 int(info.digits),
        "point":                  float(info.point),
        "trade_contract_size":    float(info.trade_contract_size),
        "trade_tick_size":        float(info.trade_tick_size),
        "trade_tick_value":       float(info.trade_tick_value),
        "money_per_point_per_lot": money_per_point_per_lot,
        "volume_min":             float(info.volume_min),
        "volume_max":             float(info.volume_max),
        "volume_step":            float(info.volume_step),
        "currency_base":          str(info.currency_base),
        "currency_profit":        str(info.currency_profit),
        "currency_margin":        str(info.currency_margin),
        "spread":                 int(info.spread),
        "bid":                    bid,
        "ask":                    ask,
        # Type de calcul MT5 (0=FOREX, 2=CFD, 3=CFDINDEX…) + path broker
        # ("Forex\\...", "Metals\\...", "Indices\\...", "Energies\\..."). Sert
        # à savoir si on charge une commission style ECN (FX/Métaux) ou non.
        "trade_calc_mode":        int(getattr(info, "trade_calc_mode", 0) or 0),
        "path":                   str(getattr(info, "path", "") or ""),
    }


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
    global _download_progress
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

        _download_progress = {"current": "", "index": 0, "total": len(watchlist)}
        for i, s in enumerate(watchlist):
            mt5_sym   = s.name
            canonical = _mt5_to_canonical(mt5_sym)
            _download_progress = {"current": canonical, "index": i + 1, "total": len(watchlist)}

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


# ─── /ensure_ticks_range ──────────────────────────────────────────────────────
#
# Lazy fetch MT5 ticks → DuckDB.
# Pour chaque symbole demandé, télécharge les ticks bruts manquants pour la range
# et les insère dans la table `ticks`. Utilisé par le Replay en "Mode Tick" pour
# détection précise SL/TP intra-bougie.

def _fetch_mt5_ticks(mt5_sym: str, from_dt: datetime, to_dt: datetime):
    """
    Télécharge les ticks par chunks d'1 jour (ticks volumineux : 100k-1M par jour).
    Retry 3× par chunk.
    Retourne un numpy structured array (avec dtype.names: time, bid, ask, last, volume,
    time_msc, flags, volume_real) ou None si rien.
    """
    import numpy as np
    chunk_days = 1
    cursor     = from_dt
    arrays     = []

    while cursor < to_dt:
        chunk_end = min(cursor + timedelta(days=chunk_days), to_dt)

        ticks = None
        for attempt in range(3):
            ticks = mt5.copy_ticks_range(mt5_sym, cursor, chunk_end, mt5.COPY_TICKS_ALL)
            if ticks is not None and len(ticks) > 0:
                break
            time.sleep(0.5 * (attempt + 1))

        if ticks is not None and len(ticks) > 0:
            arrays.append(ticks)

        cursor = chunk_end

    if not arrays:
        return None
    return np.concatenate(arrays)


def ensure_ticks_range(
    from_iso: str,
    to_iso:   str,
    symbols:  list[str],
) -> dict:
    """
    Lazy fetch MT5 ticks → DuckDB, pour les symboles fournis.

    Pour chaque symbol :
      1. Query DuckDB pour min/max existants
      2. Calcule les ranges manquantes
      3. Pour chaque range manquante, fetch MT5 + INSERT DB
      4. Retourne un rapport détaillé

    Lève RuntimeError si MT5 unavailable.
    """
    if not is_connected():
        raise RuntimeError("MT5 unavailable")

    if not symbols:
        return {"total_symbols": 0, "total_inserted": 0, "total_cached": 0,
                "elapsed_sec": 0.0, "symbols": []}

    dt_from       = iso_to_utc_naive(from_iso)
    dt_to         = iso_to_utc_naive(to_iso)
    dt_from_aware = dt_from.replace(tzinfo=timezone.utc)
    dt_to_aware   = dt_to.replace(tzinfo=timezone.utc)

    db_path = _resolve_db_path()
    con     = duckdb.connect(str(db_path))
    try:
        con.execute("""
            CREATE TABLE IF NOT EXISTS ticks (
                symbol VARCHAR     NOT NULL,
                time   TIMESTAMPTZ NOT NULL,
                bid    DOUBLE      NOT NULL,
                ask    DOUBLE      NOT NULL,
                last   DOUBLE,
                volume DOUBLE,
                flags  INTEGER
            )
        """)
        try:
            con.execute("CREATE INDEX IF NOT EXISTS idx_ticks_symbol_time ON ticks(symbol, time)")
        except Exception:
            pass

        # Mapping canonical → mt5 broker name (la watchlist contient les noms broker)
        watchlist    = _get_watchlist_symbols()
        mt5_by_canon = {_mt5_to_canonical(s.name): s.name for s in watchlist}

        def _to_utc_aware(x):
            if x is None: return None
            if isinstance(x, datetime):
                return x.astimezone(timezone.utc) if x.tzinfo else x.replace(tzinfo=timezone.utc)
            return None

        t_start        = time.perf_counter()
        total_inserted = 0
        total_cached   = 0
        reports        = []

        for canonical in symbols:
            mt5_sym = mt5_by_canon.get(canonical, canonical)

            # 1. Coverage existante
            row = con.execute(
                "SELECT MIN(time), MAX(time) FROM ticks WHERE symbol = ?",
                [canonical],
            ).fetchone()
            db_min, db_max = row if row else (None, None)
            db_min_aware   = _to_utc_aware(db_min)
            db_max_aware   = _to_utc_aware(db_max)

            missing_naive = []
            if db_min_aware is None:
                missing_naive.append((dt_from, dt_to))
            else:
                if dt_from_aware < db_min_aware:
                    missing_naive.append((dt_from, db_min_aware.astimezone(timezone.utc).replace(tzinfo=None)))
                if dt_to_aware > db_max_aware:
                    missing_naive.append((db_max_aware.astimezone(timezone.utc).replace(tzinfo=None), dt_to))

            if not missing_naive:
                cached = con.execute(
                    "SELECT COUNT(*) FROM ticks WHERE symbol = ? AND time >= ? AND time <= ?",
                    [canonical, dt_from_aware, dt_to_aware],
                ).fetchone()[0]
                reports.append({"symbol": canonical, "inserted": 0, "cached": int(cached), "error": ""})
                total_cached += int(cached)
                continue

            # 2. Sélection symbole + fetch
            if not mt5.symbol_select(mt5_sym, True):
                reports.append({"symbol": canonical, "inserted": 0, "cached": 0,
                                "error": "symbol_select failed"})
                continue

            inserted_this = 0
            error_msg     = ""
            for (range_from, range_to) in missing_naive:
                try:
                    ticks_arr = _fetch_mt5_ticks(mt5_sym, range_from, range_to)
                except Exception as e:
                    error_msg = f"fetch: {e}"
                    continue
                if ticks_arr is None or len(ticks_arr) == 0:
                    continue

                # ticks_arr est un numpy structured array (préserve les noms de colonnes)
                df = pd.DataFrame(ticks_arr)
                if "time_msc" in df.columns:
                    df["time_q"] = pd.to_datetime(df["time_msc"], unit="ms", utc=True).dt.tz_convert(QUEBEC_TZ)
                else:
                    df["time_q"] = pd.to_datetime(df["time"], unit="s", utc=True).dt.tz_convert(QUEBEC_TZ)

                df = df.drop_duplicates(subset=["time_q", "bid", "ask"]).sort_values("time_q")

                insert_df = pd.DataFrame({
                    "symbol": canonical,
                    "time":   df["time_q"],
                    "bid":    df["bid"].round(5)  if "bid"    in df.columns else 0.0,
                    "ask":    df["ask"].round(5)  if "ask"    in df.columns else 0.0,
                    "last":   df["last"].round(5) if "last"   in df.columns else 0.0,
                    "volume": df["volume"].astype("float64") if "volume" in df.columns else 0.0,
                    "flags":  df["flags"].astype("int32")    if "flags"  in df.columns else 0,
                })

                # Idempotent : supprime les ticks déjà présents dans cette range avant insert.
                # Évite les doublons quand on re-fetch un boundary (db_max → range_from).
                rf_aware = range_from.replace(tzinfo=timezone.utc)
                rt_aware = range_to.replace(tzinfo=timezone.utc)
                con.execute(
                    "DELETE FROM ticks WHERE symbol = ? AND time >= ? AND time < ?",
                    [canonical, rf_aware, rt_aware],
                )
                con.execute("INSERT INTO ticks SELECT * FROM insert_df")
                inserted_this += len(insert_df)

            total_inserted += inserted_this

            cached_count = con.execute(
                "SELECT COUNT(*) FROM ticks WHERE symbol = ? AND time >= ? AND time <= ?",
                [canonical, dt_from_aware, dt_to_aware],
            ).fetchone()[0]
            cached_for_range = max(0, int(cached_count) - inserted_this)
            total_cached    += cached_for_range

            reports.append({
                "symbol":   canonical,
                "inserted": inserted_this,
                "cached":   cached_for_range,
                "error":    error_msg,
            })

        return {
            "total_symbols":  len(symbols),
            "total_inserted": total_inserted,
            "total_cached":   total_cached,
            "elapsed_sec":    round(time.perf_counter() - t_start, 2),
            "symbols":        reports,
        }
    finally:
        con.close()


# ─── Exceptions personnalisées ────────────────────────────────────────────────

class MarketClosedError(Exception):
    pass


class TicketNotFoundError(Exception):
    def __init__(self, ticket: int):
        self.ticket = ticket
        super().__init__(f"Ticket {ticket} introuvable")
