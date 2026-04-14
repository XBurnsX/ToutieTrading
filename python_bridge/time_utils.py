"""
Utilitaires timezone — toutes les conversions UTC → heure Québec.
Règle absolue : rien ne sort du bridge sans être converti en heure Québec offset-aware.
"""

from datetime import datetime, timezone
import pytz

QUEBEC_TZ = pytz.timezone("America/Toronto")


def unix_to_quebec(unix_seconds: int) -> datetime:
    """Unix timestamp (secondes, UTC) → datetime heure Québec offset-aware."""
    dt_utc = datetime.fromtimestamp(unix_seconds, tz=timezone.utc)
    return dt_utc.astimezone(QUEBEC_TZ)


def utc_now_quebec() -> datetime:
    """Retourne l'heure actuelle en heure Québec offset-aware."""
    return datetime.now(tz=timezone.utc).astimezone(QUEBEC_TZ)


def iso_to_utc_naive(iso_str: str) -> datetime:
    """
    Parse une string ISO 8601 offset-aware (envoyée par le C#)
    et retourne un datetime UTC naive pour MT5.
    Ex: '2024-01-23T08:45:00-05:00' → datetime(2024, 01, 23, 13, 45, 0)
    """
    dt = datetime.fromisoformat(iso_str)
    if dt.tzinfo is not None:
        dt = dt.astimezone(timezone.utc).replace(tzinfo=None)
    return dt


def format_iso(dt: datetime) -> str:
    """datetime offset-aware → string ISO 8601. Ex: '2024-01-23T08:45:00-05:00'"""
    return dt.isoformat()
