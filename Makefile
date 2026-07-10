# Facade over docker compose — every target delegates to containers.
# Local requirements: git, Docker (and make, shipped with the OS).
.DEFAULT_GOAL := help

# Pinned tooling images (see docs/practices.md — tooling images are pinned).
HADOLINT_IMAGE = hadolint/hadolint@sha256:27086352fd5e1907ea2b934eb1023f217c5ae087992eb59fde121dce9c9ff21e
TRIVY_IMAGE = aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f

# Named volumes holding in-container dependencies. They are seeded from the
# image only on first creation, so they must be dropped on rebuild.
DEP_VOLUMES = verbatim-intelligence_frontend_node_modules \
              verbatim-intelligence_backend_nuget

.PHONY: help up down rebuild logs ps lint audit test outdated

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

lint: ## Run all linters (frontend Biome, backend dotnet format, Dockerfiles hadolint)
	docker compose run --rm --no-deps frontend npm run lint
	docker compose run --rm --no-deps backend dotnet build VerbatimIntelligence.slnx
	docker compose run --rm --no-deps backend dotnet format VerbatimIntelligence.slnx --verify-no-changes
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(HADOLINT_IMAGE) hadolint frontend/Dockerfile.dev backend/Dockerfile.dev

audit: ## Security checks (Trivy: misconfigurations + lockfile CVEs)
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(TRIVY_IMAGE) config --exit-code 1 .
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(TRIVY_IMAGE) fs --exit-code 1 --scanners vuln,secret .

test: ## Run all tests
	docker compose run --rm --no-deps frontend npm run test:unit -- --run
	docker compose run --rm --no-deps backend dotnet test VerbatimIntelligence.slnx

outdated: ## Report outdated dependencies per brick
	-docker compose run --rm --no-deps frontend npm outdated
	-docker compose run --rm --no-deps backend dotnet list VerbatimIntelligence.slnx package --outdated
