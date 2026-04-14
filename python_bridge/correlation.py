"""
Store in-memory pour l'anti-double-envoi d'ordres.
correlation_id (UUID v4, généré côté C#) → réponse originale de l'ordre.

Si le bridge redémarre, le store est réinitialisé — acceptable car le C#
redémarre aussi et génère de nouveaux correlation_id pour chaque signal.
"""

from typing import Optional

# { correlation_id: {"ticket": int, "fill_price": float, "time": str} }
_store: dict[str, dict] = {}


def get(correlation_id: str) -> Optional[dict]:
    return _store.get(correlation_id)


def save(correlation_id: str, ticket: int, fill_price: float, fill_time: str) -> None:
    _store[correlation_id] = {
        "ticket": ticket,
        "fill_price": fill_price,
        "time": fill_time,
    }


def clear() -> None:
    _store.clear()
