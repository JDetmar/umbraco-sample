# Canonical commands (ES-052). Humans, agents and CI use the same targets.
# Prerequisites: GNU Make, the .NET SDK pinned in global.json, Docker (or Podman), and gitleaks 8.30.1 for `make secrets`.

SHELL := bash
.SHELLFLAGS := -eo pipefail -c

SLN := UmbracoSample.slnx
CONFIG ?= Release
GITLEAKS ?= gitleaks

.PHONY: help bootstrap restore lockfiles-check build format format-check frontend frontend-test frontend-audit test test-integration image smoke validate secrets vuln run clean

help: ## List targets
	@grep -E '^[a-z-]+:.*##' $(MAKEFILE_LIST) | awk -F':.*## ' '{printf "  %-16s %s\n", $$1, $$2}'

bootstrap: restore ## First-time setup: restore tools and packages
	dotnet tool restore

restore: ## Restore NuGet packages from lock files
	dotnet restore $(SLN)

lockfiles-check: restore ## Fail if restore changed or created a lock file (ES-013)
	git diff --exit-code -- '*packages.lock.json'
	@untracked="$$(git ls-files --others --exclude-standard -- '*packages.lock.json')"; \
	if [ -n "$$untracked" ]; then echo "Lock files not committed: $$untracked"; exit 1; fi

frontend: ## Build design tokens and the site stylesheet (needs the Node version in .nvmrc)
	cd frontend && npm ci && npm run build

frontend-test: ## Token contrast and stylesheet rules
	cd frontend && npm ci && npm test

frontend-audit: ## Fail on npm advisories of moderate or above that the ledger has not assessed (ES-016)
	cd frontend && npm run audit

build: ## Build the solution (warnings are errors)
	dotnet build $(SLN) -c $(CONFIG) --no-restore

format: ## Apply formatting and code style fixes
	dotnet format $(SLN)

format-check: ## Fail if formatting or code style would change
	dotnet format $(SLN) --verify-no-changes --no-restore

FAST_TEST_PROJECTS := $(filter-out %Integration.Tests,$(wildcard tests/*.Tests))

test: ## Fast tests: unit and policy tests, no containers
	@for project in $(FAST_TEST_PROJECTS); do \
		dotnet test --project "$$project" -c $(CONFIG) --no-build || exit 1; \
	done

test-integration: ## Integration tests: starts the real Aspire stack (needs Docker)
	dotnet test --project tests/UmbracoSample.Integration.Tests -c $(CONFIG) --no-build

image: frontend ## Build the site container image as umbraco-sample:local
	docker build -f src/UmbracoSample.Web/Dockerfile --build-arg SOURCE_REVISION="$$(git rev-parse HEAD)" -t umbraco-sample:local .

smoke: ## Smoke-test a running site: make smoke URL=https://host
	scripts/smoke.sh "$(URL)"

secrets: ## Scan committed history on every fetched ref for secrets
	$(GITLEAKS) git --redact --no-banner .

vuln: ## Fail on known-vulnerable NuGet packages, including transitive ones
	@mkdir -p artifacts
	dotnet list $(SLN) package --vulnerable --include-transitive | tee artifacts/vulnerable-packages.txt
	@if grep -q "has the following vulnerable packages" artifacts/vulnerable-packages.txt; then \
		echo "Vulnerable packages found (ES-016)."; exit 1; fi

validate: lockfiles-check format-check frontend frontend-test build test secrets ## Fast pre-handoff check (ES-053); CI runs the same

run: frontend ## Run the whole stack locally through Aspire (site and SQL Server)
	dotnet run --project src/UmbracoSample.AppHost

clean: ## Remove build output
	dotnet clean $(SLN) -c $(CONFIG)
