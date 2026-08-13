# OmniAgent Console — local helpers (no push)

.PHONY: smoke smoke-long smoke-e2e build-fe test-be test-fe up

BASE ?= http://localhost:5080
E2E_BASE_URL ?= http://localhost:4210
TIMEOUT_SEC ?= 120

## Live panel smoke: needs stack up, API key, ≥1 agent group
smoke:
	BASE=$(BASE) TIMEOUT_SEC=$(TIMEOUT_SEC) ./scripts/smoke-panel.sh

## Longer free-tier wait
smoke-long:
	BASE=$(BASE) TIMEOUT_SEC=300 ./scripts/smoke-panel.sh

## Playwright UI smoke (system Chrome channel; stack must be up)
smoke-e2e:
	cd frontend && E2E_BASE_URL=$(E2E_BASE_URL) npx playwright test

build-fe:
	cd frontend && npm run build

test-be:
	dotnet test backend/tests/OmniAgentConsole.UnitTests/OmniAgentConsole.UnitTests.csproj

test-fe:
	cd frontend && npm test

up:
	docker compose up -d --build
