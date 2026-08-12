# OmniAgent Console / Multi-Agent Studio

Web-based, terminal-feel multi-agent studio. Backend is .NET 10, frontend is Angular 21; provider integration uses OpenAI-compatible chat completion (NVIDIA NIM, OpenAI, Gemini OpenAI-compatible endpoints, and other compatible providers).

You enter a prompt; the agent chain Planner → Research → Coder → Reviewer → (optional single Coder **fix loop**) → Ops Monitor runs, generated code files are written into the `workspace/` folder with a real project structure, and the full flow is watched on a realtime console.

A second product surface — **Groups + Panel** — runs moderated multi-persona discussions (moderator + commentators with For/Against stances), independent of the coding pipeline. Studio tasks can pick a **pipeline** (`full` / `coder` / `plan-code-review`). Finished panels support **audience votes**; Groups has **cast templates** (3-1, 2v2). See [CHANGELOG.md](CHANGELOG.md) and [docs/ROADMAP.md](docs/ROADMAP.md).

## Current scope

### Architecture
- .NET 10 API + separate `OmniAgentConsole.Worker` process (task execution is independent of the API process; an API restart does not kill a running task)
- PostgreSQL persistence and EF Core migrations
- RabbitMQ task queue: **at-least-once delivery** with manual ack/nack — on host shutdown the message is NACK'd and requeued; when the worker comes back the task runs again; user cancel is ACK'd (verified with a live interrupt scenario)
- **Poison-message protection**: if a redelivered message fails again with an unexpected error, the task is finalized as Failed + ACK — no infinite requeue loop (effective max 2 deliveries)
- **Cross-process cancel**: cancel is published immediately to the worker over Redis `task-cancellations` and aborts the in-flight model HTTP call (verified live in <1s, 2026-07-20). DB status is written as `Cancelled` before the token fires so the worker classifies it as a user cancel (ACK); the Coder tool loop also re-checks DB status every iteration as belt-and-braces
- API startup recovery runs only in **single-process mode** (no RabbitMQ) — with a separate worker, an API restart does not touch live Running tasks; dead runs are recovered via queue redelivery
- SignalR realtime console stream; worker → API event flow over Redis pub/sub
- Docker Compose for frontend, API, agent-worker, PostgreSQL, Redis, RabbitMQ, and Vault; optional OpenSearch profile
- **Agentic tool loop (Coder)**: the Coder agent runs a Claude Code–style tool loop — the model calls `write_file` / `read_file` / `list_files` and builds the project file-by-file in short iterations (OpenAI-compatible function calling). Each tool call streams live to the console; each iteration is logged as its own model call for usage; for models that never call tools, markdown fence export remains the fallback
- **Reviewer → Coder fix loop**: when the Reviewer produces actionable findings, the Coder gets **one** extra pass to fix only those findings on the workspace (`Fix loop started` / `Fix loop skipped` console events); Ops Monitor still runs last
- **Docker production contract**: for every backend project the Coder emits `Dockerfile` + `docker-compose.yml` (service `app`, `${HOST_PORT:-18080}`, `/health`); the Workspace runner brings the stack up against that contract

### Skill Library (project convention packs)
- 20 built-in skills: **Backend** (Node/Express/TS, Go, .NET, Java Spring Boot, Python FastAPI), **Frontend** (Angular, React, Flutter), **Data** (PostgreSQL + Migrations, ORM, MongoDB, Redis Caching, RabbitMQ Messaging), **Security** (JWT Authentication), **Quality** (Input Validation, Unit Tests, REST Conventions, Health Checks, README & Docs), **Packaging** (Dockerized Service)
- Skills selected in Studio are injected into **every agent's system prompt for that task as mandatory conventions**; click a chip to see what it does
- **Auto-suggestion from the prompt**: while typing (600ms debounce) the prompt is matched against skill keywords; matching skills are auto-selected with a ✨ mark; dismiss an unwanted chip with one click. When the prompt is ambiguous, follow-up questions appear ("Which language/framework?", "Which database?")
- Skills are managed under Settings → Skill Library; each skill's `keywords` field controls what triggers auto-suggestion (your custom skills become suggestable too)

### Model management
- Model Registry: add/remove models from the UI; the **"Sync from NVIDIA"** button imports the provider's OpenAI-compatible `GET /v1/models` catalog (all API models on build.nvidia.com, ~119) in one click
- Per-agent max token limit is configurable up to 200,000 (Agents screen)
- **Fallback model chain**: up to 2 fallback models per agent (Agents screen); if the primary hits timeout / rate-limit / 404 and similar errors, the next model in the chain is tried automatically (401 auth is the only exception — the same key would affect the whole chain); a "falling back" event is emitted to the console

### Security
- Console API Key middleware: when `CONSOLE_API_KEY` is set, all API + SignalR access requires the key (X-Api-Key, Bearer, or `access_token` query for SignalR); comparison is timing-safe. When unset, local dev stays anonymous
- API Credentials Manager: provider API keys are managed from the UI; **raw keys never leave the API** (masked preview + `apiKeyConfigured` flag); an empty key on update keeps the stored key
- Workspace path guard: every file read/write/delete is locked to the `/workspace` root — `..` traversal, absolute paths outside the root, backslash tricks, and symlink escapes are rejected; filenames coming from model output go through the same guard
- Prompt/response redaction: `InputSanitizer` — NVIDIA/OpenAI/Anthropic (`sk-ant-`)/Google (`AIza`)/GitHub (`ghp_`, `github_pat_`)/Slack tokens, JWTs, Bearer headers, and `PASSWORD=` / `JWT_SECRET=` / `"api_key": "..."` style key=value assignments are masked (value only; the key name stays readable). Free-form secrets outside these patterns can theoretically slip through — do not put secrets in prompts
- **Infra ports default to loopback**: Postgres/Redis/RabbitMQ/Vault/OpenSearch host ports bind to `127.0.0.1` (`INFRA_BIND_ADDRESS` overrides, see `.env.example`) — services that carry plaintext credentials are not exposed on the LAN

### Studio and outputs
- Coder writes files **directly into the workspace** (tool loop, max 24 iterations / 50 files / 1M chars per file); every path goes through WorkspacePathGuard
- Fallback for models without tool support: fenced markdown blocks and `// filepath:`-annotated streams export the old way; blocks with no detectable filename land under `output/`
- Workspace screen: browse, read, delete generated files; selecting a folder shows the **Project run** panel — copyable `docker compose up` command, **Start/Stop** (API → Docker socket), port assignment (`18000–18999`), `/health` link
- **Workspace test**: project type is auto-detected as `api` / `web` / `hybrid`; mini Postman for APIs (method/path/headers/body + SSRF-safe proxy), **Open in browser** for web; route chips (`/health` + source scan or `openapi.json`)
- **Swagger / OpenAPI skill**: in the Studio skill library; when selected, Coder emits Swagger UI (`/docs`) + `/openapi.json` + sample request bodies — try them via Workspace tester chips and **Open Swagger**
- Task history, task detail, dashboard; usage tracking (model, tokens, latency, errors)
- Agents screen: agent definitions (model, system prompt, provider, credential binding, max tokens) managed from the UI

### Agent Groups & moderated Panel (2026-08-12)
- **Groups** (`/groups`, `/groups/{guid}`): create a cast of speakers. Each member has:
  - **Role**: `Moderator` (opens, introduces roster) or `Commentator` (debates)
  - **Stance**: `Neutral` | `For` | `Against` | `Custom` + optional stance label (thesis)
  - **Persona** system prompt, model chain, timeout (~1 minute speaking budget)
- **Panel** (`/panel`, `/panel/{sessionGuid}`): pick a group, set a topic/title → **Start**. Every session is persisted with its own GUID (bookmarkable).
- Runtime: worker runs a single round in roster order (moderators first). Before speech, a **roster briefing** is written to the stream (who is on stage, missions, stances). Models are instructed not to invent guests and to map stances onto the *actual* topic when labels were written for another debate.
- Fail-forward: a failed guest does not abort the whole panel; remaining speakers still get the floor.
- Credentials: panel turns use the member credential or the **default OmniAgent/NVIDIA** credential; Settings key save dual-writes Vault paths used after a Vault **dev-mode** restart.
- Shared-lab: group config writes are instructor-only; panel create/start stays session-scoped like tasks.
- **Rounds** (1–3) on Start; after finish, **Continue** with a user follow-up for another roster pass.
- **Export .md** transcript per session; **Clone group** and **Open in Panel** from Groups.
- **History** lists both Studio tasks and panel sessions (filterable), each with GUID deep links.
- **Home** (`/`): recent activity, quick links, first-run checklist.
- **Panel conversation filter**: default stream shows speeches/topic/floor only; “All events” shows model noise.
- **Durable lab secrets**: API keys mirrored to `./data/secrets` (gitignored) so Vault `-dev` restarts do not wipe keys.
- **Home** key badge (configured + masked preview); Panel **Conversation** filter, **is speaking…** bar, auto-scroll.
- **Smoke**: `make smoke` (or `BASE=http://localhost:5080 ./scripts/smoke-panel.sh`) — needs API key + ≥1 group; free-tier may still be Running when it PASSes with completed turns.
- Panel: floor **progress bar**, **Delete** / **Clear finished**, queue-busy hint, TTS **Read**, collapsed roster briefing; PR draft at [docs/PR_BODY.md](docs/PR_BODY.md) (push only when approved).

```bash
# Example flow after stack is up and Settings → OmniAgent API key is saved:
# 1) Open http://localhost:4210/groups  → define speakers
# 2) Open http://localhost:4210/panel   → Start with a topic (optional 2–3 rounds)
# 3) Reopen via http://localhost:4210/panel/{session-guid}
# 4) Export transcript or Continue with a follow-up question
```

## Recommended models (NVIDIA NIM free endpoint)

Catalog candidates were tested with real calls (latency + output format, 2026-07-17). Per-agent recommendations:

| Agent | Recommended model | Why |
|---|---|---|
| **Coder** | `deepseek-ai/deepseek-v4-flash` | Reliable function calling; verified live that it builds multi-file projects via `write_file` in the agentic tool loop (2026-07-20). |
| **Planner** | `openai/gpt-oss-120b` | Strong at planning/reasoning, ~2.4s. Puts reasoning in a separate field so `content` stays clean. |
| **Reviewer** | `openai/gpt-oss-120b` | Deliberately a **different model family than Coder** — does not share the same blind spots; adds cross-check value. |
| **Research** | `nvidia/nemotron-3-super-120b-a12b` | NVIDIA's current agentic flagship, ~2.6s; good at extracting context/constraints. |
| **Ops Monitor** | `meta/llama-3.1-8b-instruct` | 8B is enough and fast for short operational summaries; a large model here wastes tokens/latency. Alternative: `stepfun-ai/step-3.7-flash` (~2.1s). |

**Avoid** (from testing):
- `qwen/qwen3.5-122b-a10b` — removed from the catalog; endpoint returns **410 Gone** (found 2026-07-20; previously Coder primary)
- `moonshotai/kimi-k2.6`, `nvidia/nemotron-nano-3-30b-a3b` — listed in the catalog but the endpoint returns **404** (not deployed)
- `nvidia/llama-3.3-nemotron-super-49b-v1.5`, `nvidia/nvidia-nemotron-nano-9b-v2` — write the answer into the `reasoning` field and leave `content` **empty**. The provider now falls back to `reasoning_content`/`reasoning` when content is empty (2026-07-20), so these models became usable — still not recommended as primary until live-verified
- `deepseek-ai/deepseek-v4-pro`, `z-ai/glm-5.2`, `mistralai/mistral-medium-3.5-128b` — free-tier queue waits of 60–90s+; regularly hit the 120s agent timeout

Applied chains (primary → fallback 1 → fallback 2):

- Planner: `gpt-oss-120b` → `nemotron-3-super-120b-a12b` → `llama-3.1-8b-instruct`
- Research: `nemotron-3-super-120b-a12b` → `step-3.7-flash` → `llama-3.1-8b-instruct`
- Coder: `deepseek-v4-flash` → `gpt-oss-120b` → `nemotron-3-super-120b-a12b` (timeout 300s)
- Reviewer: `gpt-oss-120b` → `nemotron-3-super-120b-a12b` → `deepseek-v4-flash` (timeout 180s)
- Ops Monitor: `llama-3.1-8b-instruct` → `step-3.7-flash` → `minimax-m3`

Note: Latencies vary with free-tier load; the catalog is updated over time. After "Sync from NVIDIA" you can try new models from the Agents screen.

## Project structure

```text
backend/src/OmniAgentConsole.Api             # REST API, SignalR hub, middleware, startup seed/sync
backend/src/OmniAgentConsole.Application     # DTOs, guards (WorkspacePathGuard), SkillSuggestionEngine, InputSanitizer
backend/src/OmniAgentConsole.Domain          # Entities and enums
backend/src/OmniAgentConsole.Infrastructure  # EF Core, RabbitMQ, Redis, Vault, provider, orchestrator
backend/src/OmniAgentConsole.Worker          # Task execution process (queue consumer + orchestrator)
backend/tests/OmniAgentConsole.UnitTests     # xUnit (guard, export, requeue, suggestion, sanitizer tests)
frontend                                     # Angular 21 studio
workspace                                    # agent output files (not committed to git)
```

## Requirements

- Docker Desktop
- .NET 10 SDK
- Node.js 24 recommended

If you run everything with Docker only, local .NET and Node installs are not required.

## Configuration

```bash
cp .env.example .env
```

### OmniAgent (default provider) API key

1. After the app is up, enter the key from the `Settings` screen. The key is written into HashiCorp Vault.
2. Alternatively add `OMNIAGENT_API_KEY=...` to `.env`. If Vault dev mode is reset, the backend uses this env value as a fallback.

### Other provider keys

Added via `Settings → API Credentials Manager` (OpenAI, Anthropic, Gemini, Ollama, Custom/OpenAI-compatible). Keys are stored in PostgreSQL; API responses only return a masked preview. If a credential is marked "Default", agents without a bound credential fall through to that key. Calls use the OpenAI-compatible `/chat/completions` format; Anthropic native API is not supported yet (see Notes).

### Console API Key

If `CONSOLE_API_KEY=...` is set in `.env`, the backend requires this key on all REST and SignalR requests; the frontend keeps the value entered in the Settings "Console API Key" field in localStorage and attaches it to requests. Left empty (local-dev default), access is anonymous. Set it in every shared/production-like environment.

Local Vault details:

- Address: `http://localhost:8201`
- Token: `dev-root-token`
- Secret path: `secret/data/providers/omniagent`
- Secret reference: `secret/providers/omniagent#apiKey`

Dev-mode Vault is not suitable for production.

## Deployment models

The same codebase supports two profiles; behavior is selected by environment variable:

| Profile | When | Isolation | Required |
|--------|----------|-----------|---------|
| **Laptop-only** (default) | Each student/user runs `docker compose up` on their own machine | Not needed — single user | Nothing; today's behavior |
| **Shared-lab** (opt-in) | One server, class connects to the same URL | Session + task ownership + `/workspace/sessions/{id}/` prefix | `SHARED_LAB=true` + `CONSOLE_API_KEY` |

- **Laptop-only**: flag off, no extra identity/session friction. Infra ports already bind to `127.0.0.1`.
- **Shared-lab**: with `SHARED_LAB=true` the session header is required, tasks are filtered by session owner, workspace is locked to the session root, and Settings/Credentials write endpoints are locked (instructor manages via `CONSOLE_API_KEY`). If the flag is on and `CONSOLE_API_KEY` is empty the app **fails fast** — anonymous shared deployment is not possible by mistake.

> ✅ **Status**: Shared-lab profile is implemented and live-verified (2026-07-20): two different sessions cannot see/cancel each other's tasks (404), workspace is locked under `/workspace/sessions/{id}/`, students cannot write credentials/agents/skills/settings (403, skill auto-suggest stays open), and `SHARED_LAB=true` + empty `CONSOLE_API_KEY` refuses to start. Usage: set `SHARED_LAB=true` and `CONSOLE_API_KEY=<instructor-key>` in `.env`, restart the stack; the instructor enters the key in Settings → Console API Key to gain admin rights.

## Running with Docker

```bash
docker compose up -d --build
```

The Compose project name is pinned in `docker-compose.yml` (`name: omni-agent-console`); containers are named `omni-agent-console-*`. The `agent-worker` service starts automatically; tasks are dispatched to the worker over RabbitMQ, console events flow API-bound via Redis pub/sub and from there to the UI over SignalR.

Services:

- Frontend: `http://localhost:4210`
- Backend health: `http://localhost:5080/health`
- RabbitMQ UI: `http://localhost:15673`
- Vault API/UI: `http://localhost:8201`

Logs / status:

```bash
docker compose logs -f backend-api agent-worker frontend
docker compose ps
curl http://localhost:5080/health
```

### Windows / Docker Desktop performance

When Mac is fine but Windows locks up the PC, the usual cause is **Docker Desktop + WSL2 + bind mount**, not application code. This repo also used to over-poll: Studio pulled full task detail every 2s and the workspace tree walked into folders like `node_modules`, inflating CPU — those were mitigated (status endpoint, tree skip, resource limits).

Practical tips for Windows users:

1. **Docker Desktop resources**: Settings → Resources — do not give more than half of the machine's RAM; keep CPU limits reasonable (e.g. 4 CPU, 4–6 GB).
2. **WSL2 + filesystem**: put the repo on the WSL filesystem when possible (`~/projects/...`), not under Windows `C:\Users\...`. The `./workspace` bind mount over Windows NTFS is very slow and CPU-heavy.
3. **Resource limits**: `docker-compose.yml` defines soft limits per service (`API_MEM_LIMIT`, `WORKER_MEM_LIMIT`, …). Raise them via `.env` if a task OOMs.
4. **Workspace runner**: one-click `docker compose up` starts nested containers. If you do not need it in class/on a laptop, set `WORKSPACE_RUNNER_ENABLED=false` in `.env`.
5. **Stop unused stacks**: `docker compose down` — RabbitMQ + Vault + Postgres + worker otherwise keep eating memory in the background.
6. **Antivirus**: exclude `workspace/`, the Docker WSL distro, and `node_modules` from real-time scanning.

Optional OpenSearch:

```bash
docker compose --profile observability up -d --build
```

In the Docker profile, task dispatch defaults to RabbitMQ; to run a local backend without RabbitMQ, appsettings defaults remain `InMemory`.

## Usage flow

1. Open `http://localhost:4210`.
2. On the `Settings` screen, enter the OmniAgent API key (and/or add provider keys via API Credentials Manager); verify with `Check Health`.
3. Optional: `Settings → Model Registry → Sync from NVIDIA` to import the full catalog, then assign agent models on the `Agents` screen (see the recommendation table above).
4. On the `Studio` screen pick a working directory and write the prompt — matching skills are suggested automatically; add/remove chips manually if needed.

Sample prompt:

```text
Write a high-performance Go REST API that fetches user data from a PostgreSQL
database and caches frequently queried data in Redis. Add a retry mechanism for
the Redis connection. Also prepare a docker-compose file that brings the whole
stack up and a health check endpoint.
```

(This prompt auto-selects the Go REST API, Redis Caching, PostgreSQL + Migrations, Dockerized Service, and Health Checks skills.)

5. Start with `Run Task`; watch agent steps on the realtime console.
6. Inspect generated files on the `Workspace` screen and metrics on `History` / `Task Detail` / `Dashboard`.

## Local development

Backend API:

```bash
dotnet run --project backend/src/OmniAgentConsole.Api/OmniAgentConsole.Api.csproj
```

Worker (required for task execution; the API alone does not run tasks):

```bash
dotnet run --project backend/src/OmniAgentConsole.Worker/OmniAgentConsole.Worker.csproj
```

Frontend:

```bash
cd frontend
npm install
npm start
```

Test/build:

```bash
dotnet build OmniAgentConsole.slnx
dotnet test backend/tests/OmniAgentConsole.UnitTests/OmniAgentConsole.UnitTests.csproj
cd frontend && npm test          # Vitest unit tests (27)
cd frontend && npm run build
```

## Notes

- Task execution lives in a separate worker process; tasks interrupted by a worker restart re-run automatically via NACK/requeue. API startup recovery only runs in single-process mode (no RabbitMQ) — with a separate-worker topology, an API restart does not touch live tasks.
- Prompt/response records get basic PII/secret masking via `InputSanitizer`; provider raw metadata is still stored without redaction for now.
- The Credentials API never returns raw keys. When Vault is available (Docker default), provider keys are stored at `providers/credentials/{id}`; the DB keeps only `ApiKeySecretPath` + `KeyLastFour` (startup migrate moves existing plaintext keys). In lab mode without Vault the legacy `ApiKey` column is used.
- Multi-provider support is limited to OpenAI-compatible endpoints (NVIDIA NIM, OpenAI, Gemini's OpenAI-compatible endpoint, Ollama, Custom). Anthropic native API schema is not supported.
- When `content` is empty the provider falls back to `reasoning_content`/`reasoning`; reasoning-only models now produce usable output instead of empty responses.
- Schema lives entirely in EF migrations (`InitialCreate` + `CredentialsSkillsAndFallbacks` + `SharedLabTaskOwnership` + `CredentialSecretRefs`); idempotent SQL applies cleanly to both fresh and previously patched databases. Startup does data seed + (if Vault is open) credential plaintext→secret migrate. To generate a migration: `dotnet tool restore && dotnet ef migrations add <Name> --project backend/src/OmniAgentConsole.Infrastructure --startup-project backend/src/OmniAgentConsole.Api --output-dir Persistence/Migrations`
- NVIDIA catalog sync does not return context-window info (`/v1/models` does not expose it); critical models can be entered manually in Settings.

## Roadmap

The roadmap, backlog, and closed-findings archive live in [docs/ROADMAP.md](docs/ROADMAP.md).  
Release notes: [CHANGELOG.md](CHANGELOG.md).

Completed (highlights): dual deployment / shared-lab, orchestrator refactor, frontend Vitest, credential Vault secret-ref, Reviewer→Coder fix loop, **Agent Groups + moderated Panel MVP (2026-08-12)**.
