import uuid
from datetime import UTC, datetime
from typing import TYPE_CHECKING

import psycopg
import pytest
from testcontainers.postgres import PostgresContainer
from testcontainers.redis import RedisContainer

if TYPE_CHECKING:
    from collections.abc import Iterator

    import redis

from ai_worker.processing import process_analysis
from ai_worker.redis_queue import PENDING_ANALYSES_KEY, pop_analysis_id

POSTGRES_IMAGE = (
    "postgres:18-alpine"
    "@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15"
)
REDIS_IMAGE = (
    "redis:8-alpine"
    "@sha256:9d317178eceac8454a2284a9e6df2466b93c745529947f0cd42a0fa9609d7005"
)

# Provisional mirror of the EF Core migration (the schema owner), until the
# cross-language contract test lands (see docs/architecture.md). The worker
# only reads and writes `analyses`; the ingestion tables (uploads, verbatims)
# are omitted because it never touches them. source_filename and verbatim_count
# carry their database defaults so the worker's minimal INSERT below stays
# valid — the same expand/contract move as the backend migration.
ANALYSES_DDL = """
CREATE TABLE users (
    id uuid PRIMARY KEY,
    email varchar(320) NOT NULL UNIQUE,
    created_at timestamptz NOT NULL
);
CREATE TABLE analyses (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    status varchar(16) NOT NULL,
    source_filename varchar(255) NOT NULL DEFAULT '',
    verbatim_count integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    CONSTRAINT ck_analyses_status
        CHECK (status IN ('pending', 'running', 'succeeded', 'failed'))
)
"""


@pytest.fixture(scope="module")
def database_url() -> Iterator[str]:
    with PostgresContainer(POSTGRES_IMAGE, driver=None) as postgres:
        yield postgres.get_connection_url()


@pytest.fixture
def connection(database_url: str) -> Iterator[psycopg.Connection]:
    with psycopg.connect(database_url) as conn:
        conn.execute("DROP TABLE IF EXISTS analyses")
        conn.execute("DROP TABLE IF EXISTS users")
        conn.execute(ANALYSES_DDL)
        conn.commit()
        yield conn


@pytest.fixture(scope="module")
def redis_client() -> Iterator[redis.Redis]:
    with RedisContainer(REDIS_IMAGE) as container:
        yield container.get_client(decode_responses=True)


def insert_analysis(connection: psycopg.Connection, status: str) -> uuid.UUID:
    analysis_id = uuid.uuid4()
    user_id = uuid.uuid4()
    connection.execute(
        "INSERT INTO users (id, email, created_at) VALUES (%s, %s, %s)",
        (user_id, f"{user_id}@example.test", datetime.now(tz=UTC)),
    )
    connection.execute(
        "INSERT INTO analyses (id, user_id, status, created_at)"
        " VALUES (%s, %s, %s, %s)",
        (analysis_id, user_id, status, datetime.now(tz=UTC)),
    )
    connection.commit()
    return analysis_id


def status_of(connection: psycopg.Connection, analysis_id: uuid.UUID) -> str:
    row = connection.execute(
        "SELECT status FROM analyses WHERE id = %s", (analysis_id,)
    ).fetchone()
    assert row is not None
    return str(row[0])


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
    assert status_of(connection, analysis_id) == "succeeded"


def test_process_does_not_claim_an_already_running_analysis(
    connection: psycopg.Connection,
) -> None:
    analysis_id = insert_analysis(connection, "running")

    assert process_analysis(connection, str(analysis_id)) is False
    assert status_of(connection, analysis_id) == "running"


def test_process_rejects_a_malformed_id(connection: psycopg.Connection) -> None:
    assert process_analysis(connection, "not-a-uuid") is False
