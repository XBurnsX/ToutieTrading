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
