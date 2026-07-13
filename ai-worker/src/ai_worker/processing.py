import logging
import uuid
from typing import TYPE_CHECKING

from ai_worker import pipeline

if TYPE_CHECKING:
    import psycopg

logger = logging.getLogger(__name__)


def process_analysis(connection: psycopg.Connection, analysis_id: str) -> bool:
    """Claim the analysis and run it to completion.

    Returns False when there is nothing to do: malformed id, unknown id, or
    an analysis some other worker already claimed. A pipeline failure is
    still work done (the analysis lands in `failed` with its error), so it
    returns True.
    """
    try:
        claimed_id = uuid.UUID(analysis_id)
    except ValueError:
        logger.warning("ignoring malformed analysis id %r", analysis_id)
        return False

    if not _claim(connection, claimed_id):
        return False

    _purge_previous_attempt(connection, claimed_id)
    try:
        run_pipeline(connection, claimed_id)
    except Exception as exc:
        _fail(connection, claimed_id, str(exc) or type(exc).__name__)
        logger.exception("analysis %s failed", claimed_id)
    else:
        _complete(connection, claimed_id)
    return True


def run_pipeline(connection: psycopg.Connection, analysis_id: uuid.UUID) -> None:
    """Run the LLM pipeline on a claimed analysis (see ai_worker.pipeline)."""
    pipeline.run(connection, analysis_id)


def beat(connection: psycopg.Connection, analysis_id: uuid.UUID) -> None:
    """Record a sign of life; the reaper spares analyses with a fresh one.

    Called between pipeline steps, so a step must stay well under the
    reaper's staleness timeout (see ai_worker.reaper).
    """
    connection.execute(
        "UPDATE analyses SET heartbeat_at = now() WHERE id = %s AND status = 'running'",
        (analysis_id,),
    )
    connection.commit()


def _claim(connection: psycopg.Connection, analysis_id: uuid.UUID) -> bool:
    """Atomically take ownership: one UPDATE, two workers can never win.

    The claim also opens a clean attempt: heartbeat stamped, attempt
    counted, the previous attempt's error and progress reset.
    """
    row = connection.execute(
        "UPDATE analyses SET status = 'running', heartbeat_at = now(),"
        " attempts = attempts + 1, error = NULL, processed_count = 0"
        " WHERE id = %s AND status = 'pending' RETURNING id",
        (analysis_id,),
    ).fetchone()
    connection.commit()
    return row is not None


def _purge_previous_attempt(
    connection: psycopg.Connection, analysis_id: uuid.UUID
) -> None:
    """Idempotence: drop what a dead attempt wrote, replaying never duplicates.

    theme_verbatims rows follow their theme by cascade.
    """
    connection.execute("DELETE FROM themes WHERE analysis_id = %s", (analysis_id,))
    connection.commit()


def _fail(connection: psycopg.Connection, analysis_id: uuid.UUID, error: str) -> None:
    # Partial writes of the failed attempt must not survive it.
    connection.rollback()
    connection.execute(
        "UPDATE analyses SET status = 'failed', error = %s"
        " WHERE id = %s AND status = 'running'",
        (error, analysis_id),
    )
    connection.commit()


def _complete(connection: psycopg.Connection, analysis_id: uuid.UUID) -> None:
    connection.execute(
        "UPDATE analyses SET status = 'succeeded' WHERE id = %s AND status = 'running'",
        (analysis_id,),
    )
    connection.commit()
