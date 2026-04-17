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
    profit: float = 0.0
    margin: float = 0.0
    free_margin: float = 0.0
    margin_level: float = 0.0
    login: int = 0
    server: str = ""


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


# ─── /symbol_info ─────────────────────────────────────────────────────────────

class SymbolInfoResponse(BaseModel):
    mt5_name:            str        # Nom broker-natif (ex: "EURUSD.m")
    canonical_name:      str        # Nom canonique (ex: "EURUSD")
    digits:              int        # Nombre de décimales du prix
    point:               float      # Plus petit incrément de prix (ex: 0.00001)
    trade_contract_size: float      # Taille du contrat (ex: 100000 pour FX, 1 pour indices)
    trade_tick_size:     float      # Plus petit changement de prix tradable
    trade_tick_value:    float      # Valeur monétaire d'un tick (devise du compte)
    money_per_point_per_lot: float = 0.0  # Profit MT5 réel pour 1 lot bougé d'1 unité de prix
    volume_min:          float      # Lot minimum
    volume_max:          float      # Lot maximum
    volume_step:         float      # Pas du lot
    currency_base:       str        # Devise de base
    currency_profit:     str        # Devise de profit
    currency_margin:     str        # Devise de marge
    spread:              int        # Spread courant en points (multiplier par point pour prix)
    bid:                 float      # Bid courant
    ask:                 float      # Ask courant
    trade_calc_mode:     int = 0    # 0=FOREX, 1=FUTURES, 2=CFD, 3=CFDINDEX, 4=CFDLEVERAGE, 5=FOREX_NO_LEVERAGE
    path:                str = ""   # Chemin broker ex "Forex\\EURUSD" — sert à classer Métal/Indice/Énergie


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


# ─── /ensure_ticks_range ──────────────────────────────────────────────────────

class EnsureTicksRangeRequest(BaseModel):
    from_iso: str            # ISO 8601 offset-aware heure Québec
    to_iso:   str            # ISO 8601 offset-aware heure Québec
    symbols:  List[str]      # canonical names (ex: ["EURUSD", "XAUUSD"])


class TicksFetchReport(BaseModel):
    symbol:   str
    inserted: int
    cached:   int
    error:    str


class EnsureTicksRangeResponse(BaseModel):
    total_symbols:  int
    total_inserted: int
    total_cached:   int
    elapsed_sec:    float
    symbols:        List[TicksFetchReport]


# ─── Erreurs ──────────────────────────────────────────────────────────────────

class ErrorResponse(BaseModel):
    error: str
    reason: Optional[str] = None
