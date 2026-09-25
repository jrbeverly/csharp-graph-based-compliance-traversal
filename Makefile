.DEFAULT_GOAL := help

SOLUTION := GraphBasedComplianceTraversal.sln
CLI_PROJECT := src/GraphBasedComplianceTraversal.Cli/GraphBasedComplianceTraversal.Cli.csproj
CONFIGURATION ?= Release

.PHONY: help restore build test test-update-goldens run demo

help: ## Show available commands and CLI usage
	@awk 'BEGIN { FS = ":.*## " } /^[a-zA-Z_-]+:.*## / { printf "  %-12s %s\n", $$1, $$2 }' $(MAKEFILE_LIST)
	@dotnet run --project $(CLI_PROJECT) --configuration $(CONFIGURATION) --no-launch-profile -- --help

restore: ## Restore .NET dependencies
	dotnet restore $(SOLUTION)

build: restore ## Build the solution
	dotnet build $(SOLUTION) --configuration $(CONFIGURATION) --no-restore

test: restore ## Build and run all tests
	dotnet test $(SOLUTION) --configuration $(CONFIGURATION) --no-restore

test-update-goldens: restore ## Regenerate golden files from current output (review the diff before committing)
	UPDATE_GOLDENS=1 dotnet test $(SOLUTION) --configuration $(CONFIGURATION) --no-restore

run: ## Run the CLI (pass arguments with ARGS="...")
	dotnet run --project $(CLI_PROJECT) --configuration $(CONFIGURATION) --no-launch-profile -- $(ARGS)

demo: ## Run the end-to-end demonstration (offline, deterministic; see docs/demo.md)
	dotnet run --project $(CLI_PROJECT) --configuration $(CONFIGURATION) --no-launch-profile -- demo
