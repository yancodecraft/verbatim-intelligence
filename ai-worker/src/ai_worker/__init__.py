import logging
import os
import time
from typing import NoReturn

import psycopg
import redis

from ai_worker.processing import process_analysis
from ai_worker.reaper import reap
from ai_worker.redis_queue import POP_TIMEOUT_SECONDS, pop_analysis_id

RETRY_DELAY_SECONDS = 5

# Kept well under the reaper's staleness timeout so recovery lags a stuck
# analysis by at most a minute, not another full timeout.
REAP_INTERVAL_SECONDS = 60

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
        # 0.0 makes the first turn reap: recovery must not wait an interval.
        next_reap = 0.0
        while True:
            if time.monotonic() >= next_reap:
                reap(connection, client)
                next_reap = time.monotonic() + REAP_INTERVAL_SECONDS
            analysis_id = pop_analysis_id(client)
            if analysis_id is None:
                continue
            if process_analysis(connection, analysis_id):
                logger.info("analysis %s concluded", analysis_id)
            else:
                logger.warning("analysis %s was not claimable", analysis_id)
