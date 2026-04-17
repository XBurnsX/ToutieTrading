"""
ToutieTrader — Python FastAPI Mini-bot MT5
Seul interlocuteur de MT5. Le C# ne touche jamais MT5 directement.

Endpoints :
    GET  /status
    GET  /account
    GET  /candles
    POST /order
    POST /close_order

Règles absolues :
    - Toutes les timestamps retournées = heure Québec ISO 8601 offset-aware
    - MT5 retourne UTC → converti dans mt5_service avant envoi au C#
    - Zéro retry automatique sur aucun endpoint
    - correlation_id déjà reçu → retourne ticket original sans renvoyer l'ordre

Démarrage :
    uvicorn main:app --host 127.0.0.1 --port 8000 --reload
"""

from contextlib import asynccontextmanager
from typing import Optional

from fastapi import FastAPI, Query
from fastapi.responses import JSONResponse

import mt5_service
from mt5_service import MarketClosedError, TicketNotFoundError
from models import (
    StatusResponse,
    AccountResponse,
    CandleResponse,
    OrderRequest,
    OrderResponse,
    CloseOrderRequest,
    CloseOrderResponse,
    EnsureCandlesRangeRequest,
    EnsureCandlesRangeResponse,
    EnsureTicksRangeRequest,
    EnsureTicksRangeResponse,
    WatchlistSymbol,
    SymbolInfoResponse,
)


# ─── Lifespan — init/shutdown MT5 ─────────────────────────────────────────────

@asynccontextmanager
async def lifespan(app: FastAPI):
    mt5_service.initialize()
    yield
    mt5_service.shutdown()


app = FastAPI(
    title="ToutieTrader MT5 Bridge",
    version="1.0.0",
    lifespan=lifespan,
)


# ─── GET /status ──────────────────────────────────────────────────────────────

@app.get("/status", response_model=StatusResponse)
async def status():
    """
    Vérifie que Python tourne et que MT5 est connecté.
    Appelé toutes les 5s par le C#. Timeout C# : 3s.
    """
    return mt5_service.get_status()


# ─── GET /account ─────────────────────────────────────────────────────────────

@app.get("/account", response_model=AccountResponse)
async def account():
    """
    Retourne balance, equity, drawdown_percent, currency.
    Timeout C# : 5s.
    """
    try:
        return mt5_service.get_account()
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={"error": str(e)})


# ─── GET /candles ─────────────────────────────────────────────────────────────

@app.get("/candles", response_model=list[CandleResponse])
async def candles(
    symbol:    str,
    timeframe: str,
    count:     Optional[int] = Query(default=None),
    from_:     Optional[str] = Query(default=None, alias="from"),
    to:        Optional[str] = Query(default=None),
):
    """
    Retourne les bougies d'un symbole.
    - ?symbol=EURUSD&timeframe=M15&count=200
    - ?symbol=EURUSD&timeframe=H1&from=2024-01-01T00:00:00-05:00&to=2024-03-01T00:00:00-05:00
    Timestamps retournées en heure Québec offset-aware. Prix 5 décimales.
    Timeout C# : 10s.
    """
    try:
        return mt5_service.get_candles(
            symbol=symbol,
            timeframe=timeframe,
            count=count,
            from_iso=from_,
            to_iso=to,
        )
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={"error": str(e)})
    except ValueError as e:
        return JSONResponse(status_code=422, content={"error": str(e)})


# ─── POST /order ──────────────────────────────────────────────────────────────

@app.post("/order", response_model=OrderResponse)
async def order(req: OrderRequest):
    """
    Envoie un market order via MT5.
    correlation_id déjà reçu → retourne le ticket original sans renvoi.
    Rejet → 422. Marché fermé → 422 "Market closed".
    Pas de retry. Timeout C# : 10s.
    """
    try:
        return mt5_service.send_order(
            correlation_id=req.correlation_id,
            symbol=req.symbol,
            direction=req.direction,
            lot_size=req.lot_size,
            sl=req.sl,
            tp=req.tp,
        )
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={"error": str(e)})
    except MarketClosedError:
        return JSONResponse(status_code=422, content={"error": "Market closed"})
    except ValueError as e:
        return JSONResponse(status_code=422, content={"error": "Order rejected", "reason": str(e)})


# ─── GET /watchlist ───────────────────────────────────────────────────────────

@app.get("/watchlist", response_model=list[WatchlistSymbol])
async def watchlist():
    """
    Retourne la watchlist MT5 (Market Watch) avec nom broker-natif + nom canonique.
    Appelé par le C# au démarrage de ReplayPage pour peupler le dropdown symbole
    sans qu'aucune donnée ne soit encore présente dans candles.db.
    Timeout C# : 5s.
    """
    try:
        return mt5_service.get_watchlist()
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={"error": str(e)})


# ─── GET /symbol_info ─────────────────────────────────────────────────────────

@app.get("/symbol_info", response_model=SymbolInfoResponse)
async def symbol_info(symbol: str):
    """
    Retourne les métadonnées MT5 d'un symbole (point, contract size, tick value/size,
    volume_min/max/step, devises, spread courant, bid/ask).
    Accepte nom canonique (ex: "EURUSD") ou broker-natif (ex: "EURUSD.m").
    Utilisé par le C# pour calculer un lot/SL/TP réaliste sans rien hardcoder.
    Timeout C# : 5s.
    """
    try:
        return mt5_service.get_symbol_info(symbol)
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={"error": str(e)})
    except ValueError as e:
        return JSONResponse(status_code=404, content={"error": str(e)})


# ─── POST /ensure_candles_range ───────────────────────────────────────────────

@app.post("/ensure_candles_range", response_model=EnsureCandlesRangeResponse)
async def ensure_candles_range(req: EnsureCandlesRangeRequest):
    """
    Lazy fetch MT5 → DuckDB.

    Pour chaque symbol dans la watchlist MT5 (Market Watch) × chaque TF demandé :
      - Check coverage existante dans candles.db
      - Télécharge seulement les ranges manquantes depuis MT5
      - INSERT les nouvelles bougies dans candles.db

    Appelé par ReplayService.StartAsync() juste avant de charger les données.
    Premier run d'une date range = lent (fetch MT5). Runs suivants = instant (cache hit).

    Timeout C# : 600s (peut être très long la première fois).
    """
    try:
        return mt5_service.ensure_candles_range(
            from_iso=req.from_iso,
            to_iso=req.to_iso,
            timeframes=req.timeframes,
        )
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={"error": str(e)})
    except ValueError as e:
        return JSONResponse(status_code=422, content={"error": str(e)})


# ─── POST /ensure_ticks_range ─────────────────────────────────────────────────

@app.post("/ensure_ticks_range", response_model=EnsureTicksRangeResponse)
async def ensure_ticks_range(req: EnsureTicksRangeRequest):
    """
    Lazy fetch MT5 ticks → DuckDB pour les symboles fournis.
    Utilisé par le Replay en "Mode Tick" pour détection précise SL/TP intra-bougie.
    Premier run d'une date range = lent (fetch MT5). Runs suivants = instant.
    Timeout C# : 600s.
    """
    try:
        return mt5_service.ensure_ticks_range(
            from_iso=req.from_iso,
            to_iso=req.to_iso,
            symbols=req.symbols,
        )
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={"error": str(e)})
    except ValueError as e:
        return JSONResponse(status_code=422, content={"error": str(e)})


# ─── POST /close_order ────────────────────────────────────────────────────────

@app.post("/close_order", response_model=CloseOrderResponse)
async def close_order(req: CloseOrderRequest):
    """
    Ferme une position ouverte par son ticket MT5.
    Ticket introuvable → 404.
    Timeout C# : 10s.
    """
    try:
        return mt5_service.close_order(req.ticket)
    except RuntimeError as e:
        return JSONResponse(status_code=503, content={"error": str(e)})
    except TicketNotFoundError as e:
        return JSONResponse(status_code=404, content={"error": "Ticket not found"})
    except ValueError as e:
        return JSONResponse(status_code=422, content={"error": str(e)})
