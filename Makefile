# Facade over docker compose — every target delegates to containers.
# Local requirements: git, Docker (and make, shipped with the OS).
.DEFAULT_GOAL := help

# Pinned tooling images (see docs/practices.md — tooling images are pinned).
HADOLINT_IMAGE = hadolint/hadolint@sha256:27086352fd5e1907ea2b934eb1023f217c5ae087992eb59fde121dce9c9ff21e
TRIVY_IMAGE = aquasec/trivy@sha256:cffe3f5161a47a6823fbd23d985795b3ed72a4c806da4c4df16266c02accdd6f
# Keep in sync with @playwright/test in frontend/package.json.
PLAYWRIGHT_IMAGE = mcr.microsoft.com/playwright@sha256:5b8f294aff9041b7191c34a4bab3ac270157a28774d4b0660e9743297b697e48
TERRAFORM_IMAGE = hashicorp/terraform@sha256:7ae513256f7ce67879e218ae8593d6fbe216ec9e123abe6c94e4e10704857963

# Terraform runs in a container; Scaleway credentials are read from the scw
# CLI config at invocation time and the state backend is S3-compatible
# Object Storage. Nothing sensitive ever enters the repo.
SCW_CONFIG = $(HOME)/.config/scw/config.yaml
TF_ENV = AWS_ACCESS_KEY_ID=$$(awk '$$1=="access_key:"{print $$2}' "$(SCW_CONFIG)") \
         AWS_SECRET_ACCESS_KEY=$$(awk '$$1=="secret_key:"{print $$2}' "$(SCW_CONFIG)") \
         TF_VAR_ssh_public_key="$$(cat "$(HOME)/.ssh/verbatim_ed25519.pub")"
TF_RUN = docker run --rm \
         -v "$(CURDIR)/infra/terraform":/infra \
         -v "$(SCW_CONFIG)":/root/.config/scw/config.yaml:ro \
         -e AWS_ACCESS_KEY_ID -e AWS_SECRET_ACCESS_KEY -e TF_VAR_ssh_public_key \
         $(TERRAFORM_IMAGE)

# Named volumes holding in-container dependencies. They are seeded from the
# image only on first creation, so they must be dropped on rebuild.
DEP_VOLUMES = verbatim-intelligence_frontend_node_modules \
              verbatim-intelligence_backend_nuget \
              verbatim-intelligence_ai_worker_venv

.PHONY: help up down rebuild logs ps psql lint audit test e2e ci outdated \
        infra-bootstrap infra-init infra-plan infra-apply infra-output configure deploy

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
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(HADOLINT_IMAGE) hadolint \
	  frontend/Dockerfile.dev frontend/Dockerfile \
	  backend/Dockerfile.dev backend/Dockerfile \
	  ai-worker/Dockerfile.dev ai-worker/Dockerfile \
	  infra/ansible/Dockerfile

audit: ## Security checks (Trivy: misconfigurations + lockfile CVEs)
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(TRIVY_IMAGE) config --exit-code 1 --ignorefile /repo/.trivyignore.yaml .
	docker run --rm -v "$(CURDIR)":/repo -w /repo $(TRIVY_IMAGE) fs --exit-code 1 --scanners vuln,secret --ignorefile /repo/.trivyignore.yaml .

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

e2e: ## Run the e2e suite against the live dev stack (needs make up)
	docker run --rm --network verbatim-intelligence_default \
	  -e PLAYWRIGHT_BASE_URL=http://frontend:5173 \
	  -v "$(CURDIR)/frontend":/work \
	  -v verbatim-intelligence_frontend_node_modules:/work/node_modules \
	  -w /work $(PLAYWRIGHT_IMAGE) npx playwright test

# The CI pipeline, runnable locally: GitHub Actions orchestrates these same
# targets — anything green here is green there.
ci: ## Run the full pipeline: lint, tests, build, e2e, audit
	$(MAKE) lint
	$(MAKE) test
	docker compose build
	$(MAKE) up
	$(MAKE) e2e
	$(MAKE) audit

infra-bootstrap: ## One-time: create the Terraform state bucket
	@$(TF_ENV) $(TF_RUN) -chdir=/infra/bootstrap init -input=false
	@$(TF_ENV) $(TF_RUN) -chdir=/infra/bootstrap apply -input=false -auto-approve

infra-init: ## Initialize Terraform (providers, remote state)
	@$(TF_ENV) $(TF_RUN) -chdir=/infra init -input=false

infra-plan: ## Show the infrastructure changes Terraform would make
	@$(TF_ENV) $(TF_RUN) -chdir=/infra plan -input=false

infra-apply: ## Apply the infrastructure changes
	@$(TF_ENV) $(TF_RUN) -chdir=/infra apply -input=false -auto-approve

infra-output: ## Show Terraform outputs (public IP)
	@$(TF_ENV) $(TF_RUN) -chdir=/infra output

# TAG selects the image tag to deploy (default latest); rollback is
# TAG=<previous-sha> make deploy.
configure deploy: ## Converge the server: hardening, Docker, the app stack
	docker build -q -t verbatim-ansible infra/ansible
	docker run --rm \
	  -v "$(CURDIR)/infra/ansible":/ansible \
	  -v "$(HOME)/.ssh/verbatim_ed25519":/keys/verbatim_ed25519:ro \
	  -v "$(HOME)/.config/verbatim-intelligence/prod-secrets.yml":/secrets/prod.yml:ro \
	  verbatim-ansible site.yml $(if $(TAG),-e tag=$(TAG),)

outdated: ## Report outdated dependencies per brick
	-docker compose run --rm --no-deps frontend npm outdated
	-docker compose run --rm --no-deps backend dotnet list VerbatimIntelligence.slnx package --outdated
	-docker compose run --rm --no-deps ai-worker uv tree --outdated --depth 1
