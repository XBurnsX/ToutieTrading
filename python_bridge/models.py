"""
Pydantic models — contrats request/response des endpoints FastAPI.
Schémas verrouillés selon bot-plan.html section 5.
"""

from pydantic import BaseModel
from typing import Optional


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


# ─── Erreurs ──────────────────────────────────────────────────────────────────

class ErrorResponse(BaseModel):
    error: str
    reason: Optional[str] = None
