import logging
from typing import TYPE_CHECKING

from ai_worker.redis_queue import PENDING_ANALYSES_KEY

if TYPE_CHECKING:
    import psycopg
    import redis

# A live worker beats between pipeline steps; a heartbeat older than this
# means the worker died mid-analysis (see docs/architecture.md, "Résilience
# du traitement asynchrone").
HEARTBEAT_TIMEOUT_SECONDS = 300

# Claims of one analysis before the reaper gives up on it.
MAX_ATTEMPTS = 3

# Shown to the user on the analysis, so it must stand on its own.
DEAD_WORKER_ERROR = "The analysis was interrupted repeatedly; it was given up."

logger = logging.getLogger(__name__)


def reap(connection: psycopg.Connection, client: redis.Redis) -> None:
    """Recover analyses stranded by dead workers or lost queue signals.

    Runs periodically in the worker loop. Republishing can duplicate a queue
    signal; the atomic claim makes that harmless.
    """
    _fail_exhausted(connection)
    requeued = _requeue_stale_running(connection)
    forgotten = _republish_forgotten_pending(connection)
    for analysis_id in requeued + forgotten:
        client.rpush(PENDING_ANALYSES_KEY, analysis_id)


def _fail_exhausted(connection: psycopg.Connection) -> None:
    rows = connection.execute(
        "UPDATE analyses SET status = 'failed', error = %(error)s"
        " WHERE status = 'running'"
        " AND heartbeat_at < now() - make_interval(secs => %(timeout)s)"
        " AND attempts >= %(max_attempts)s"
        " RETURNING id",
        {
            "error": DEAD_WORKER_ERROR,
            "timeout": HEARTBEAT_TIMEOUT_SECONDS,
            "max_attempts": MAX_ATTEMPTS,
        },
    ).fetchall()
    connection.commit()
    for (analysis_id,) in rows:
        logger.error(
            "analysis %s failed: worker died %s times", analysis_id, MAX_ATTEMPTS
        )


def _requeue_stale_running(connection: psycopg.Connection) -> list[str]:
    # heartbeat_at doubles as "last touched by the reaper": stamping it here
    # keeps the analysis out of the forgotten-pending sweep below until a
    # whole timeout has passed again.
    rows = connection.execute(
        "UPDATE analyses SET status = 'pending', heartbeat_at = now()"
        " WHERE status = 'running'"
        " AND heartbeat_at < now() - make_interval(secs => %(timeout)s)"
        " AND attempts < %(max_attempts)s"
        " RETURNING id",
        {"timeout": HEARTBEAT_TIMEOUT_SECONDS, "max_attempts": MAX_ATTEMPTS},
    ).fetchall()
    connection.commit()
    for (analysis_id,) in rows:
        logger.warning("requeued stale analysis %s", analysis_id)
    return [str(analysis_id) for (analysis_id,) in rows]


def _republish_forgotten_pending(connection: psycopg.Connection) -> list[str]:
    rows = connection.execute(
        "UPDATE analyses SET heartbeat_at = now()"
        " WHERE status = 'pending'"
        " AND coalesce(heartbeat_at, created_at)"
        "     < now() - make_interval(secs => %(timeout)s)"
        " RETURNING id",
        {"timeout": HEARTBEAT_TIMEOUT_SECONDS},
    ).fetchall()
    connection.commit()
    for (analysis_id,) in rows:
        logger.warning("republished forgotten pending analysis %s", analysis_id)
    return [str(analysis_id) for (analysis_id,) in rows]
