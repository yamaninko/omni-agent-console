# OmniAgent Console — local helpers (no push)

.PHONY: smoke smoke-long build-fe test-be test-fe up

BASE ?= http://localhost:5080
TIMEOUT_SEC ?= 120

## Live panel smoke: needs stack up, API key, ≥1 agent group
smoke:
	BASE=$(BASE) TIMEOUT_SEC=$(TIMEOUT_SEC) ./scripts/smoke-panel.sh

## Longer free-tier wait
smoke-long:
	BASE=$(BASE) TIMEOUT_SEC=300 ./scripts/smoke-panel.sh

build-fe:
	cd frontend && npm run build

test-be:
	dotnet test backend/tests/OmniAgentConsole.UnitTests/OmniAgentConsole.UnitTests.csproj

test-fe:
	cd frontend && npm test

up:
	docker compose up -d --build
