# Contributing to OmniAgent Console

Thanks for helping improve the multi-agent studio.

## Development setup

```bash
cp .env.example .env
# set OMNIAGENT_API_KEY for live model calls
docker compose up -d --build
```

- UI: http://localhost:4210  
- API: http://localhost:5080  

Local unit tests (no Docker required for pure logic tests):

```bash
dotnet test backend/tests/OmniAgentConsole.UnitTests/OmniAgentConsole.UnitTests.csproj
cd frontend && npm test
```

## Pull requests

1. Prefer focused PRs (one feature or fix).  
2. Update `CHANGELOG.md` under `[Unreleased]` when behavior changes.  
3. Keep secrets out of commits (`.env`, Vault data, `data/secrets`).  
4. CI runs backend build/tests and frontend build on PRs.

## Good first issues

Look for issues labeled **`good first issue`**. Typical starter tasks:

- Docs / README clarity  
- UI copy / a11y  
- Extra unit tests for pure policies (`TaskPipelinePolicy`, `PanelVoteStore`, …)  
- Studio preset or group template additions  

## Code map

| Area | Location |
|------|----------|
| REST + SignalR | `backend/src/OmniAgentConsole.Api` |
| Domain + DTOs | `Domain`, `Application` |
| Orchestrator / Panel / queue | `Infrastructure/Runtime` |
| Angular UI | `frontend/src/app/features` |

## Style

- Match existing patterns (controllers thin, policies pure/static when possible).  
- Prefer English UI/docs for public surfaces; Turkish notes are welcome in docs.  
- Do not expand scope of unrelated refactors in the same PR.
