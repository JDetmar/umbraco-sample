# Canonical commands (ES-052). Humans, agents and CI use the same targets.
# Prerequisites: GNU Make, the .NET SDK pinned in global.json, Docker (or Podman), and gitleaks 8.30.1 for `make secrets`.

SHELL := bash
.SHELLFLAGS := -eo pipefail -c

SLN := UmbracoSample.slnx
CONFIG ?= Release
GITLEAKS ?= gitleaks

.PHONY: help bootstrap restore lockfiles-check build format format-check test validate secrets vuln run clean

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

build: ## Build the solution (warnings are errors)
	dotnet build $(SLN) -c $(CONFIG) --no-restore

format: ## Apply formatting and code style fixes
	dotnet format $(SLN)

format-check: ## Fail if formatting or code style would change
	dotnet format $(SLN) --verify-no-changes --no-restore

test: ## Fast tests: unit and policy tests, no containers
	dotnet test --solution $(SLN) -c $(CONFIG) --no-build --filter-not-trait "Category=Integration"

secrets: ## Scan committed history on every fetched ref for secrets
	$(GITLEAKS) git --redact --no-banner .

vuln: ## Fail on known-vulnerable NuGet packages, including transitive ones
	@mkdir -p artifacts
	dotnet list $(SLN) package --vulnerable --include-transitive | tee artifacts/vulnerable-packages.txt
	@if grep -q "has the following vulnerable packages" artifacts/vulnerable-packages.txt; then \
		echo "Vulnerable packages found (ES-016)."; exit 1; fi

validate: lockfiles-check format-check build test secrets ## Fast pre-handoff check (ES-053); CI runs the same

run: ## Run the whole stack locally through Aspire (site and SQL Server)
	dotnet run --project src/UmbracoSample.AppHost

clean: ## Remove build output
	dotnet clean $(SLN) -c $(CONFIG)
