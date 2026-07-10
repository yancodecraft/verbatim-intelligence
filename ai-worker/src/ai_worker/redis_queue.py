from typing import TYPE_CHECKING

if TYPE_CHECKING:
    import redis

# The queue carries analysis ids, nothing else — state lives in the
# database. The key name is part of the contract with the backend
# (RedisKeys.PendingAnalyses on the C# side).
PENDING_ANALYSES_KEY = "analyses:pending"

# Server-side wait of the blocking pop. The client's socket timeout must
# stay above it, or the socket gives up before the server answers.
POP_TIMEOUT_SECONDS = 5


def pop_analysis_id(
    client: redis.Redis, timeout_seconds: int = POP_TIMEOUT_SECONDS
) -> str | None:
    """Block until an analysis id is queued, or return None on timeout."""
    item = client.blpop([PENDING_ANALYSES_KEY], timeout=timeout_seconds)
    if item is None:
        return None
    _, value = item
    return value.decode() if isinstance(value, bytes) else str(value)
