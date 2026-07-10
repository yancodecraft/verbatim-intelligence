import logging
import os
import time

from ai_worker.database import is_database_ready

POLL_INTERVAL_SECONDS = 5

logger = logging.getLogger(__name__)


def main() -> None:
    """Placeholder loop: proves the worker runs and reaches the database.

    The real behavior (consume analysis ids from the queue, claim and
    process them) lands with the queue.
    """
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    database_url = os.environ["DATABASE_URL"]

    logger.info("ai-worker started")
    while True:
        if is_database_ready(database_url):
            logger.info("database is ready, waiting for work")
        else:
            logger.warning("database is not reachable")
        time.sleep(POLL_INTERVAL_SECONDS)
