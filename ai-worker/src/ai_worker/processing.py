import logging
import uuid
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    import psycopg

logger = logging.getLogger(__name__)


def process_analysis(connection: psycopg.Connection, analysis_id: str) -> bool:
    """Claim the analysis and run it to completion.

    Returns False when there is nothing to do: malformed id, unknown id, or
    an analysis some other worker already claimed.
    """
    try:
        claimed_id = uuid.UUID(analysis_id)
    except ValueError:
        logger.warning("ignoring malformed analysis id %r", analysis_id)
        return False

    if not _claim(connection, claimed_id):
        return False

    # The real pipeline lands after the spike; the walking skeleton
    # completes immediately.
    _complete(connection, claimed_id)
    return True


def _claim(connection: psycopg.Connection, analysis_id: uuid.UUID) -> bool:
    """Atomically take ownership: one UPDATE, two workers can never win."""
    row = connection.execute(
        "UPDATE analyses SET status = 'running'"
        " WHERE id = %s AND status = 'pending' RETURNING id",
        (analysis_id,),
    ).fetchone()
    connection.commit()
    return row is not None


def _complete(connection: psycopg.Connection, analysis_id: uuid.UUID) -> None:
    connection.execute(
        "UPDATE analyses SET status = 'succeeded' WHERE id = %s AND status = 'running'",
        (analysis_id,),
    )
    connection.commit()
