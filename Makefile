# Canonical commands (ES-052). Humans, agents and CI use the same targets.
# Prerequisites: .NET SDK per global.json, Docker (or Podman), Node.js 22 for front-end and browser tests.

SLN := UmbracoSample.slnx
CONFIG ?= Release
GITLEAKS ?= gitleaks

.PHONY: help bootstrap restore build format format-check test validate secrets vuln run clean

help: ## List targets
	@grep -E '^[a-z-]+:.*##' $(MAKEFILE_LIST) | awk -F':.*## ' '{printf "  %-14s %s\n", $$1, $$2}'

bootstrap: restore ## First-time setup: restore tools and packages
	dotnet tool restore

restore: ## Restore NuGet packages from lock files
	dotnet restore $(SLN)

build: ## Build the solution (warnings are errors)
	dotnet build $(SLN) -c $(CONFIG) --no-restore

format: ## Apply formatting and code style fixes
	dotnet format $(SLN)

format-check: ## Fail if formatting or code style would change
	dotnet format $(SLN) --verify-no-changes --no-restore

test: ## Fast tests: unit and architecture tests, no containers
	dotnet test --solution $(SLN) -c $(CONFIG) --no-build --filter-not-trait "Category=Integration"

secrets: ## Scan the working tree and history for secrets
	$(GITLEAKS) git --redact --no-banner .

vuln: ## Fail on known-vulnerable NuGet packages, including transitive ones
	dotnet list $(SLN) package --vulnerable --include-transitive 2>&1 | tee /tmp/vuln.txt; ! grep -q "has the following vulnerable packages" /tmp/vuln.txt

validate: restore format-check build test secrets ## Fast pre-handoff check (ES-053); CI runs the same

run: ## Run the whole stack locally through Aspire (site, SQL Server)
	dotnet run --project src/UmbracoSample.AppHost

clean: ## Remove build output
	dotnet clean $(SLN) -c $(CONFIG)
