"""
Wrapper MT5 — seul fichier qui touche MetaTrader5.
Toutes les timestamps retournées sont déjà converties en heure Québec offset-aware.
"""

from typing import Optional
import MetaTrader5 as mt5

from time_utils import unix_to_quebec, utc_now_quebec, iso_to_utc_naive, format_iso
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


# ─── Exceptions personnalisées ────────────────────────────────────────────────

class MarketClosedError(Exception):
    pass


class TicketNotFoundError(Exception):
    def __init__(self, ticket: int):
        self.ticket = ticket
        super().__init__(f"Ticket {ticket} introuvable")
