from testcontainers.postgres import PostgresContainer

from ai_worker.database import is_database_ready

POSTGRES_IMAGE = (
    "postgres:18-alpine"
    "@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15"
)


def test_database_is_ready_when_postgres_is_up() -> None:
    with PostgresContainer(POSTGRES_IMAGE, driver=None) as postgres:
        assert is_database_ready(postgres.get_connection_url()) is True


def test_database_is_not_ready_when_nothing_listens() -> None:
    assert is_database_ready("postgresql://nobody@127.0.0.1:1/nothing") is False
