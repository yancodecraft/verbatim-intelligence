import logging
import os
import time
from typing import NoReturn

import psycopg
import redis

from ai_worker.processing import process_analysis
from ai_worker.redis_queue import POP_TIMEOUT_SECONDS, pop_analysis_id

RETRY_DELAY_SECONDS = 5

logger = logging.getLogger(__name__)


def main() -> NoReturn:
    """Consume queued analysis ids and process them, forever."""
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    client = redis.Redis.from_url(
        os.environ["REDIS_URL"],
        decode_responses=True,
        socket_timeout=POP_TIMEOUT_SECONDS * 2,
    )
    database_url = os.environ["DATABASE_URL"]

    logger.info("ai-worker started, waiting for analyses")
    while True:
        # A dead loop in a live container is a silent failure: transient
        # dependency errors are logged and retried, never fatal.
        try:
            _consume(client, database_url)
        except psycopg.OperationalError, redis.exceptions.RedisError:
            logger.exception(
                "dependency unavailable, retrying in %ss", RETRY_DELAY_SECONDS
            )
            time.sleep(RETRY_DELAY_SECONDS)


def _consume(client: redis.Redis, database_url: str) -> NoReturn:
    with psycopg.connect(database_url) as connection:
        while True:
            analysis_id = pop_analysis_id(client)
            if analysis_id is None:
                continue
            if process_analysis(connection, analysis_id):
                logger.info("analysis %s succeeded", analysis_id)
            else:
                logger.warning("analysis %s was not claimable", analysis_id)
