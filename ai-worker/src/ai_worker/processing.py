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

    attempt = _claim(connection, claimed_id)
    if attempt is None:
        return False

    _purge_previous_attempt(connection, claimed_id)
    try:
        run_pipeline(connection, claimed_id, attempt)
    except pipeline.SupersededError:
        # Another worker owns the analysis now (our heartbeat went stale and
        # the reaper requeued it): its attempt is the truth, we write nothing
        # — not even a failure.
        logger.warning("analysis %s was reclaimed, dropping this attempt", claimed_id)
    except Exception as exc:
        _fail(connection, claimed_id, str(exc) or type(exc).__name__, attempt)
        logger.exception("analysis %s failed", claimed_id)
    else:
        _complete(connection, claimed_id, attempt)
    return True


def run_pipeline(
    connection: psycopg.Connection, analysis_id: uuid.UUID, attempt: int
) -> None:
    """Run the LLM pipeline on a claimed analysis (see ai_worker.pipeline)."""
    pipeline.run(connection, analysis_id, attempt)


def _claim(connection: psycopg.Connection, analysis_id: uuid.UUID) -> int | None:
    """Atomically take ownership: one UPDATE, two workers can never win.

    The claim also opens a clean attempt: heartbeat stamped, attempt
    counted, the previous attempt's error and progress reset. The returned
    attempt number is the fencing token guarding every later write (see
    pipeline.SupersededError).
    """
    row = connection.execute(
        "UPDATE analyses SET status = 'running', heartbeat_at = now(),"
        " attempts = attempts + 1, error = NULL, processed_count = 0"
        " WHERE id = %s AND status = 'pending' RETURNING attempts",
        (analysis_id,),
    ).fetchone()
    connection.commit()
    return None if row is None else int(row[0])


def _purge_previous_attempt(
    connection: psycopg.Connection, analysis_id: uuid.UUID
) -> None:
    """Idempotence: drop what a dead attempt wrote, replaying never duplicates.

    theme_verbatims rows follow their theme by cascade.
    """
    connection.execute("DELETE FROM themes WHERE analysis_id = %s", (analysis_id,))
    connection.commit()


def _fail(
    connection: psycopg.Connection, analysis_id: uuid.UUID, error: str, attempt: int
) -> None:
    # Partial writes of the failed attempt must not survive it.
    connection.rollback()
    connection.execute(
        "UPDATE analyses SET status = 'failed', error = %s"
        " WHERE id = %s AND status = 'running' AND attempts = %s",
        (error, analysis_id, attempt),
    )
    connection.commit()


def _complete(
    connection: psycopg.Connection, analysis_id: uuid.UUID, attempt: int
) -> None:
    connection.execute(
        "UPDATE analyses SET status = 'succeeded'"
        " WHERE id = %s AND status = 'running' AND attempts = %s",
        (analysis_id, attempt),
    )
    connection.commit()
