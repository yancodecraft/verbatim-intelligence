import uuid
from datetime import UTC, datetime, timedelta
from typing import TYPE_CHECKING

import psycopg
import pytest
from testcontainers.postgres import PostgresContainer
from testcontainers.redis import RedisContainer

if TYPE_CHECKING:
    from collections.abc import Iterator

    import redis

POSTGRES_IMAGE = (
    "postgres:18-alpine"
    "@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15"
)
REDIS_IMAGE = (
    "redis:8-alpine"
    "@sha256:9d317178eceac8454a2284a9e6df2466b93c745529947f0cd42a0fa9609d7005"
)

# Provisional mirror of the EF Core migrations (the schema owner), until the
# cross-language contract test lands (see docs/schema.md). Only the tables the
# worker touches: analyses (claims, progress), verbatims (reads), themes and
# theme_verbatims (writes). Defaults mirror the database's so minimal INSERTs
# stay valid — the same expand/contract move as the backend migrations.
SCHEMA_DDL = """
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
    heartbeat_at timestamptz,
    attempts integer NOT NULL DEFAULT 0,
    error text,
    processed_count integer NOT NULL DEFAULT 0,
    input_tokens bigint NOT NULL DEFAULT 0,
    output_tokens bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_analyses_status
        CHECK (status IN ('pending', 'running', 'succeeded', 'failed'))
);
CREATE TABLE verbatims (
    id uuid PRIMARY KEY,
    analysis_id uuid NOT NULL REFERENCES analyses (id) ON DELETE CASCADE,
    position integer NOT NULL,
    text text NOT NULL
);
CREATE TABLE themes (
    id uuid PRIMARY KEY,
    analysis_id uuid NOT NULL REFERENCES analyses (id) ON DELETE CASCADE,
    name varchar(200) NOT NULL,
    synthesis text NOT NULL,
    position integer NOT NULL
);
CREATE TABLE theme_verbatims (
    theme_id uuid NOT NULL REFERENCES themes (id) ON DELETE CASCADE,
    verbatim_id uuid NOT NULL REFERENCES verbatims (id) ON DELETE CASCADE,
    rank integer,
    PRIMARY KEY (theme_id, verbatim_id)
)
"""


@pytest.fixture(scope="session")
def database_url() -> Iterator[str]:
    with PostgresContainer(POSTGRES_IMAGE, driver=None) as postgres:
        yield postgres.get_connection_url()


@pytest.fixture
def connection(database_url: str) -> Iterator[psycopg.Connection]:
    with psycopg.connect(database_url) as conn:
        conn.execute(
            "DROP TABLE IF EXISTS theme_verbatims, themes, verbatims, analyses, users"
        )
        conn.execute(SCHEMA_DDL)
        conn.commit()
        yield conn


@pytest.fixture(scope="session")
def _redis_container() -> Iterator[redis.Redis]:
    with RedisContainer(REDIS_IMAGE) as container:
        yield container.get_client(decode_responses=True)


@pytest.fixture
def redis_client(_redis_container: redis.Redis) -> redis.Redis:
    _redis_container.flushall()
    return _redis_container


def insert_analysis(
    connection: psycopg.Connection,
    status: str,
    *,
    attempts: int = 0,
    error: str | None = None,
    heartbeat_age_seconds: int | None = None,
    created_age_seconds: int = 0,
) -> uuid.UUID:
    """Insert an analysis owned by a fresh user, in a chosen lifecycle state."""
    analysis_id = uuid.uuid4()
    user_id = uuid.uuid4()
    now = datetime.now(tz=UTC)
    heartbeat_at = (
        now - timedelta(seconds=heartbeat_age_seconds)
        if heartbeat_age_seconds is not None
        else None
    )
    connection.execute(
        "INSERT INTO users (id, email, created_at) VALUES (%s, %s, %s)",
        (user_id, f"{user_id}@example.test", now),
    )
    connection.execute(
        "INSERT INTO analyses"
        " (id, user_id, status, created_at, heartbeat_at, attempts, error)"
        " VALUES (%s, %s, %s, %s, %s, %s, %s)",
        (
            analysis_id,
            user_id,
            status,
            now - timedelta(seconds=created_age_seconds),
            heartbeat_at,
            attempts,
            error,
        ),
    )
    connection.commit()
    return analysis_id


def fetch_analysis(
    connection: psycopg.Connection, analysis_id: uuid.UUID
) -> tuple[str, str | None, int, datetime | None]:
    """Return (status, error, attempts, heartbeat_at) of an analysis."""
    row = connection.execute(
        "SELECT status, error, attempts, heartbeat_at FROM analyses WHERE id = %s",
        (analysis_id,),
    ).fetchone()
    assert row is not None
    return (row[0], row[1], row[2], row[3])


def insert_theme(connection: psycopg.Connection, analysis_id: uuid.UUID) -> uuid.UUID:
    """Insert a theme as a dead worker's attempt would have left it."""
    theme_id = uuid.uuid4()
    connection.execute(
        "INSERT INTO themes (id, analysis_id, name, synthesis, position)"
        " VALUES (%s, %s, %s, %s, %s)",
        (theme_id, analysis_id, "Leftover theme", "From a previous attempt.", 0),
    )
    connection.commit()
    return theme_id
