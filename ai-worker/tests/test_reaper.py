from typing import TYPE_CHECKING

from conftest import fetch_analysis, insert_analysis

from ai_worker.reaper import (
    DEAD_WORKER_ERROR,
    HEARTBEAT_TIMEOUT_SECONDS,
    MAX_ATTEMPTS,
    reap,
)
from ai_worker.redis_queue import PENDING_ANALYSES_KEY

if TYPE_CHECKING:
    import psycopg
    import redis

STALE_SECONDS = HEARTBEAT_TIMEOUT_SECONDS + 60


def queued_ids(redis_client: redis.Redis) -> list[str]:
    return [str(item) for item in redis_client.lrange(PENDING_ANALYSES_KEY, 0, -1)]


def test_reap_requeues_a_stale_running_analysis(
    connection: psycopg.Connection, redis_client: redis.Redis
) -> None:
    analysis_id = insert_analysis(
        connection, "running", attempts=1, heartbeat_age_seconds=STALE_SECONDS
    )

    reap(connection, redis_client)

    status, _, attempts, _ = fetch_analysis(connection, analysis_id)
    assert status == "pending"
    assert attempts == 1
    assert queued_ids(redis_client) == [str(analysis_id)]


def test_reap_fails_a_stale_analysis_out_of_attempts(
    connection: psycopg.Connection, redis_client: redis.Redis
) -> None:
    analysis_id = insert_analysis(
        connection,
        "running",
        attempts=MAX_ATTEMPTS,
        heartbeat_age_seconds=STALE_SECONDS,
    )

    reap(connection, redis_client)

    status, error, _, _ = fetch_analysis(connection, analysis_id)
    assert status == "failed"
    assert error == DEAD_WORKER_ERROR
    assert queued_ids(redis_client) == []


def test_reap_leaves_a_live_analysis_alone(
    connection: psycopg.Connection, redis_client: redis.Redis
) -> None:
    analysis_id = insert_analysis(
        connection, "running", attempts=1, heartbeat_age_seconds=10
    )

    reap(connection, redis_client)

    status, _, _, _ = fetch_analysis(connection, analysis_id)
    assert status == "running"
    assert queued_ids(redis_client) == []


def test_reap_republishes_a_pending_analysis_whose_signal_was_lost(
    connection: psycopg.Connection, redis_client: redis.Redis
) -> None:
    # A pending analysis untouched for a whole timeout means its queue signal
    # is gone (Redis restart): republishing is safe, the atomic claim makes a
    # duplicate signal harmless.
    analysis_id = insert_analysis(
        connection, "pending", created_age_seconds=STALE_SECONDS
    )

    reap(connection, redis_client)

    status, _, _, heartbeat_at = fetch_analysis(connection, analysis_id)
    assert status == "pending"
    assert heartbeat_at is not None  # stamped to throttle the next republish
    assert queued_ids(redis_client) == [str(analysis_id)]


def test_reap_does_not_republish_a_fresh_pending_analysis(
    connection: psycopg.Connection, redis_client: redis.Redis
) -> None:
    insert_analysis(connection, "pending")

    reap(connection, redis_client)

    assert queued_ids(redis_client) == []
