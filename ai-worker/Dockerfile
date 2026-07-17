# Production image: the synced virtualenv is built with uv, then copied
# onto the matching slim Python base (the uv python3.14 variants are built
# from the same docker-library python image, so the venv symlinks resolve).
FROM ghcr.io/astral-sh/uv@sha256:7cf77f594be8042dab6daa9fe326f90962252268b4f120a7f5dccce4d947e6c1 AS build

WORKDIR /app
ENV UV_LINK_MODE=copy UV_COMPILE_BYTECODE=1
COPY pyproject.toml uv.lock README.md ./
RUN uv sync --frozen --no-dev --no-install-project
COPY src src
RUN uv sync --frozen --no-dev

FROM python:3.14-slim-bookworm@sha256:86f975aca15cf04a40b399eebede9aea7c82eae084d1f1a0a6ef6bcaae871a30

RUN groupadd --system app && useradd --system --gid app app
COPY --from=build --chown=app:app /app /app
USER app
ENV PATH="/app/.venv/bin:$PATH"

# Each probe boots a Python interpreter and opens a connection: generous
# timeout and start period, or a busy VPS flunks a healthy worker during
# deploys (seen in CI: two >5s probes at stack-recreate time, then green).
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s \
  CMD ["python", "-c", "import os, sys; from ai_worker.database import is_database_ready; sys.exit(0 if is_database_ready(os.environ['DATABASE_URL']) else 1)"]

CMD ["ai-worker"]
