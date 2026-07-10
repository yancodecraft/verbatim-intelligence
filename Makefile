# Facade over docker compose — every target delegates to containers.
# Local requirements: git, Docker (and make, shipped with the OS).
.DEFAULT_GOAL := help

# Pinned tooling images (see docs/practices.md — tooling images are pinned).
HADOLINT_IMAGE = hadolint/hadolint@sha256:27086352fd5e1907ea2b934eb1023f217c5ae087992eb59fde121dce9c9ff21e
TRIVY_IMAGE = aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f

# Named volumes holding in-container dependencies. They are seeded from the
# image only on first creation, so they must be dropped on rebuild.
DEP_VOLUMES = verbatim-intelligence_frontend_node_modules \
              verbatim-intelligence_backend_nuget \
              verbatim-intelligence_ai_worker_venv

.PHONY: help up down rebuild logs ps psql lint audit test outdated

help: ## List available targets
	@grep -E '^[a-z][a-zA-Z_-]*:.*## ' $(MAKEFILE_LIST) | awk -F ':.*## ' '{printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}'

up: ## Start the dev stack (builds images as needed)
	docker compose up -d --build

down: ## Stop the dev stack
	docker compose down

rebuild: ## Rebuild images and refresh dependency volumes (after dependency changes)
	docker compose down
	docker volume rm -f $(DEP_VOLUMES)
	docker compose up -d --build --force-recreate

logs: ## Tail all container logs
	docker compose logs -f

ps: ## Show container status
	docker compose ps

psql: ## Open a psql shell in the postgres container
	docker compose exec postgres psql -U verbatim -d verbatim

lint: ## Run all linters (Biome, dotnet build+format, ruff+mypy, hadolint)
	docker compose run --rm --no-deps frontend npm run lint
	docker compose run --rm --no-deps backend dotnet build VerbatimIntelligence.slnx
	docker compose run --rm --no-deps backend dotnet format VerbatimIntelligence.slnx --verify-no-changes
	docker compose run --rm --no-deps ai-worker uv run --frozen ruff check .
	docker compose run --rm --no-deps ai-worker uv run --frozen ruff format --check .
	docker compose run --rm --no-deps ai-worker uv run --frozen mypy src tests
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(HADOLINT_IMAGE) hadolint frontend/Dockerfile.dev backend/Dockerfile.dev ai-worker/Dockerfile.dev

audit: ## Security checks (Trivy: misconfigurations + lockfile CVEs)
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(TRIVY_IMAGE) config --exit-code 1 .
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(TRIVY_IMAGE) fs --exit-code 1 --scanners vuln,secret .

# Backend integration tests spawn throwaway Postgres containers via
# Testcontainers, hence the Docker socket mount (root-only) and the host
# override (mapped ports are published on the Docker host, not in this
# container). NUGET_PACKAGES keeps the app user's package cache in use.
test: ## Run all tests
	docker compose run --rm --no-deps frontend npm run test:unit -- --run
	docker compose run --rm --no-deps --user root \
	  -v /var/run/docker.sock:/var/run/docker.sock \
	  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
	  -e NUGET_PACKAGES=/home/app/.nuget/packages \
	  backend dotnet test VerbatimIntelligence.slnx
	docker compose run --rm --no-deps --user root \
	  -v /var/run/docker.sock:/var/run/docker.sock \
	  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
	  ai-worker uv run --frozen --no-sync pytest

outdated: ## Report outdated dependencies per brick
	-docker compose run --rm --no-deps frontend npm outdated
	-docker compose run --rm --no-deps backend dotnet list VerbatimIntelligence.slnx package --outdated
	-docker compose run --rm --no-deps ai-worker uv tree --outdated --depth 1
