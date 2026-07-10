import psycopg


def is_database_ready(database_url: str) -> bool:
    """Report whether the database accepts connections and answers queries."""
    try:
        with psycopg.connect(database_url, connect_timeout=3) as connection:
            connection.execute("SELECT 1")
    except psycopg.OperationalError:
        return False
    return True
