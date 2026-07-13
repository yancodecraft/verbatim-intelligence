import uuid
from datetime import UTC, datetime
from typing import TYPE_CHECKING

from conftest import fetch_analysis, insert_analysis, insert_theme

from ai_worker.processing import beat, process_analysis
from ai_worker.redis_queue import PENDING_ANALYSES_KEY, pop_analysis_id

if TYPE_CHECKING:
    import psycopg
    import pytest
    import redis


def test_pop_returns_none_when_queue_is_empty(redis_client: redis.Redis) -> None:
    assert pop_analysis_id(redis_client, timeout_seconds=1) is None


def test_pop_returns_pushed_id(redis_client: redis.Redis) -> None:
    analysis_id = str(uuid.uuid4())
    redis_client.rpush(PENDING_ANALYSES_KEY, analysis_id)

    assert pop_analysis_id(redis_client, timeout_seconds=1) == analysis_id


def test_process_moves_pending_analysis_to_succeeded(
    connection: psycopg.Connection,
) -> None:
    analysis_id = insert_analysis(connection, "pending")

    assert process_analysis(connection, str(analysis_id)) is True

    status, _, _, _ = fetch_analysis(connection, analysis_id)
    assert status == "succeeded"


def test_process_does_not_claim_an_already_running_analysis(
    connection: psycopg.Connection,
) -> None:
    analysis_id = insert_analysis(connection, "running")

    assert process_analysis(connection, str(analysis_id)) is False

    status, _, _, _ = fetch_analysis(connection, analysis_id)
    assert status == "running"


def test_process_rejects_a_malformed_id(connection: psycopg.Connection) -> None:
    assert process_analysis(connection, "not-a-uuid") is False


def test_claim_stamps_heartbeat_and_counts_the_attempt(
    connection: psycopg.Connection,
) -> None:
    analysis_id = insert_analysis(connection, "pending")

    assert process_analysis(connection, str(analysis_id)) is True

    _, _, attempts, heartbeat_at = fetch_analysis(connection, analysis_id)
    assert attempts == 1
    assert heartbeat_at is not None


def test_claim_clears_the_error_of_a_previous_attempt(
    connection: psycopg.Connection,
) -> None:
    analysis_id = insert_analysis(connection, "pending", error="a previous failure")

    assert process_analysis(connection, str(analysis_id)) is True

    _, error, _, _ = fetch_analysis(connection, analysis_id)
    assert error is None


def test_process_purges_leftovers_from_a_previous_attempt(
    connection: psycopg.Connection,
) -> None:
    # A worker died mid-analysis and the reaper re-queued it: themes written
    # by the dead attempt must not survive the retry (idempotence).
    analysis_id = insert_analysis(connection, "pending", attempts=1)
    insert_theme(connection, analysis_id)

    assert process_analysis(connection, str(analysis_id)) is True

    row = connection.execute(
        "SELECT count(*) FROM themes WHERE analysis_id = %s", (analysis_id,)
    ).fetchone()
    assert row is not None
    assert row[0] == 0


def test_process_marks_the_analysis_failed_when_the_pipeline_raises(
    connection: psycopg.Connection, monkeypatch: pytest.MonkeyPatch
) -> None:
    analysis_id = insert_analysis(connection, "pending")

    def explode(_connection: psycopg.Connection, _analysis_id: uuid.UUID) -> None:
        message = "the pipeline exploded"
        raise RuntimeError(message)

    monkeypatch.setattr("ai_worker.processing.run_pipeline", explode)

    assert process_analysis(connection, str(analysis_id)) is True

    status, error, _, _ = fetch_analysis(connection, analysis_id)
    assert status == "failed"
    assert error is not None
    assert "the pipeline exploded" in error


def test_beat_refreshes_the_heartbeat(connection: psycopg.Connection) -> None:
    analysis_id = insert_analysis(connection, "running", heartbeat_age_seconds=600)

    beat(connection, analysis_id)

    _, _, _, heartbeat_at = fetch_analysis(connection, analysis_id)
    assert heartbeat_at is not None
    assert (datetime.now(tz=UTC) - heartbeat_at).total_seconds() < 60
