"""
Pydantic models — contrats request/response des endpoints FastAPI.
Schémas verrouillés selon bot-plan.html section 5.
"""

from pydantic import BaseModel
from typing import Optional, List, Dict


# ─── /status ──────────────────────────────────────────────────────────────────

class StatusResponse(BaseModel):
    python: bool
    mt5: bool


# ─── /account ─────────────────────────────────────────────────────────────────

class AccountResponse(BaseModel):
    balance: float
    equity: float
    drawdown_percent: float
    currency: str


# ─── /candles ─────────────────────────────────────────────────────────────────

class CandleResponse(BaseModel):
    time: str       # ISO 8601 offset-aware heure Québec
    open: float
    high: float
    low: float
    close: float
    volume: int


# ─── /order ───────────────────────────────────────────────────────────────────

class OrderRequest(BaseModel):
    correlation_id: str     # UUID v4 généré côté C#
    symbol: str
    direction: str          # "BUY" | "SELL"
    lot_size: float
    sl: float
    tp: float


class OrderResponse(BaseModel):
    ticket: int
    fill_price: float
    time: str               # ISO 8601 offset-aware heure Québec


# ─── /close_order ─────────────────────────────────────────────────────────────

class CloseOrderRequest(BaseModel):
    ticket: int


class CloseOrderResponse(BaseModel):
    closed: bool
    close_price: float
    time: str               # ISO 8601 offset-aware heure Québec


# ─── /watchlist ───────────────────────────────────────────────────────────────

class WatchlistSymbol(BaseModel):
    mt5_name:       str       # Nom broker-natif (ex: "EURUSD.m")
    canonical_name: str       # Nom canonique normalisé (ex: "EURUSD")


# ─── /ensure_candles_range ────────────────────────────────────────────────────

class EnsureCandlesRangeRequest(BaseModel):
    from_iso:   str           # ISO 8601 offset-aware heure Québec
    to_iso:     str           # ISO 8601 offset-aware heure Québec
    timeframes: List[str]     # ex: ["M1","M5","M15","M30","H1","H4","D"]


class SymbolFetchReport(BaseModel):
    mt5_name:       str       # Nom broker-natif (ex: "EURUSD.m")
    canonical_name: str       # Nom stocké dans la DB (ex: "EURUSD")
    inserted:       Dict[str, int]   # tf -> nb bougies insérées
    cached:         Dict[str, int]   # tf -> nb bougies déjà en cache (skippées)
    errors:         Dict[str, str]   # tf -> message d'erreur


class EnsureCandlesRangeResponse(BaseModel):
    total_symbols:  int
    total_inserted: int
    total_cached:   int
    elapsed_sec:    float
    symbols:        List[SymbolFetchReport]


# ─── Erreurs ──────────────────────────────────────────────────────────────────

class ErrorResponse(BaseModel):
    error: str
    reason: Optional[str] = None
