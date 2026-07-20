# Antigravity — Agent Execution & Log

This file records the execution history, features implemented, and architectural decisions made by the Antigravity AI coding assistant for the **OMNIAGENT Agent Console** project.

---

## 🚀 Accomplished Tasks

### 1. 🧹 Refactoring & Dead Code Removal
- Removed the redundant `OmniAgentConsole.AgentRuntime` project.
- Cleared out files and references from `OmniAgentConsole.slnx`, project files, and Docker configurations.
- Refactored `AgentsController` to fetch active agents directly from `dbContext.AgentDefinitions`, making the database the single source of truth.

### 2. ⚙️ Worker Integration & Redis Event Streaming
- Conditionally configured the `TaskRunBackgroundService` as a hosted background service to run strictly in the Worker container when RabbitMQ is active.
- Integrated **Redis Pub/Sub** to stream console logs from the Worker to the API container.
- Configured a hosted `RedisConsoleEventSubscriber` on the API container that consumes these events and forwards them to SignalR clients.

### 3. 🛡️ Queue Hardening
- Swapped RabbitMQ consumption to `autoAck: false` inside `RabbitMqTaskRunQueue`.
- Implemented explicit, manual `BasicAck`/`BasicNack` calls inside `TaskRunBackgroundService`. Messages are acknowledged upon successful run or user cancellation, and nacked (requeued) on process crashes or failures.

### 4. 🔑 Api Key Authentication
- Implemented `ApiKeyMiddleware` in the ASP.NET API, supporting custom HTTP headers, Bearer tokens, and query parameter handshakes for SignalR.
- Configured the Angular frontend `apiKeyInterceptor` and connection accessTokenFactory to send the browser-local key on HTTP and SignalR WebSocket requests.
- Integrated a Console Key local storage input form in the settings page.

### 5. 🔁 Task Controls
- Allowed failed and cancelled tasks to be rerun, clearing out prior PostgreSQL logs (`AgentRuns`, `ModelCallLogs`, `ConsoleEvents`) from the database first.
- Added `TaskCancelled` to `ConsoleEventType` to prevent logs from incorrectly marking user cancellations as runtime errors.

### 6. 🧼 Data Sanitization & Cost Metrics
- Created `InputSanitizer` to mask developer API keys, passwords, and tokens (e.g. `nvapi-*`, `sk-*`, `Bearer *`) before saving logs in PostgreSQL.
- Programmed automatic startup database seeding for popular OMNIAGENT models.
- Integrated dynamic model cost estimation based on model pricing grids.

### 7. 🎛️ Dynamic Model Flows & Registry
- Refactored `StaticModelRouter` to prioritize individual agent-specific model selections.
- Added a global **Model Registry** management panel to register, list, and remove custom models directly in the database.
- Implemented **Manual Custom Model Input** on each agent configuration, allowing developers to type in custom identifiers.

### 8. 📂 VS Code Workspace File Explorer & Viewer Page
- Implemented automatic markdown code block parsing in `AgentOrchestratorService.cs` saving outputs to `/workspace`.
- Created `WorkspaceController.cs` exposing recursive file tree listings and file reader endpoints.
- Created `WorkspacePage` component and registered lazy routes for `/workspace` in the Angular frontend.
- Locked UI inputs to `/workspace/` subfolders to prevent volume-mount directory mapping failures outside Docker containers.

### 9. 🎛️ Interactive Studio Panel & Rerun Controls
- Grouped prompt inputs and paths in CSS flexbox to prevent grid layout alignment bugs.
- Integrated a Recent Tasks listing sidebar on the left to switch and load past run console logs dynamically.
- Added New Task session reset and Clear Console log buttons to the console toolbar.
- Added dynamic Rerun button to rerun legacy history tasks directly from Studio.
- Added a fallback text exporter to save raw text outputs (like READMEs) to `README.md` if no markdown code blocks are present.
- Logged the user's prompt at the beginning of `TaskStarted` console logs.

---

## 🗓️ 2026-07-17 — Security Hardening, Reliability & Skills (Claude Code session)

### 10. 🔐 Credentials & Auth Hardening
- `CredentialsController` now returns `ApiCredentialDto` — raw API keys never leave the backend (masked preview `sk-t...cdef` + `apiKeyConfigured` flag; seed placeholders report as unconfigured).
- `AgentDefinitionDto` exposes `customApiKeyConfigured` (bool) instead of the raw key; empty key on update means "keep the stored key" (both credentials and agents).
- `ApiKeyMiddleware` comparisons switched to SHA-256 + `CryptographicOperations.FixedTimeEquals` (timing-safe).
- Fixed NU1903: `Microsoft.AspNetCore.OpenApi` 10.0.10 + explicit `Microsoft.OpenApi` 2.7.5 pin (GHSA-v5pm-xwqc-g5wc).
- `.gitignore` now excludes `MEMORY.md` and `workspace/`; `CONSOLE_API_KEY` documented in `.env.example` and wired in compose.

### 11. 🧱 Workspace Path Guard
- New `WorkspacePathGuard` (Application layer): lexical normalization, root containment (sibling-prefix safe), rejection of `..`/absolute escapes, backslash traversal, and symlinked components (including dangling symlinks).
- `WorkspaceController` and `AgentOrchestratorService` both route every path through the guard; model-emitted filenames are re-validated; deleting the workspace root is blocked.
- Export limits: 50 files per export, 1M chars per file; skipped counts surfaced in console events.

### 12. 🔁 True At-Least-Once Delivery
- Orchestrator now distinguishes user cancel (DB status `Cancelled` → finalize + ACK) from host shutdown (rethrow → NACK/requeue); decision logic extracted to testable `ShouldRequeueAfterCancellation`.
- Stale `Running` agent runs are closed on redelivery ("Interrupted by worker restart").
- RabbitMQ ack/nack goes through the channel that delivered the message (delivery tags are channel-scoped); a dead channel means the broker already requeued.
- Verified live: worker stopped mid-run → task stayed `Running`, queue showed 1 ready → restart → task re-ran to `Completed`, queue drained.

### 13. 🧩 Skill Library + Auto-Suggestion
- New `SkillDefinition` entity/table with 20 seeded skills (Node/TS, Go, .NET, Java Spring, FastAPI, Angular, React, Flutter, PostgreSQL, ORM, MongoDB, Redis, RabbitMQ, JWT, Validation, Tests, REST, Health Checks, README, Docker); seed is upsert-by-name so upgrades add new skills without touching user edits.
- Selected skills are injected into every agent's system prompt as mandatory conventions; "Applied N skill(s)" console event.
- `SkillSuggestionEngine` + `POST /api/skills/suggest`: whole-word keyword matching (Turkish keywords included, "go" does not match "django") with follow-up questions when the stack or datastore is ambiguous.
- Studio: debounced auto-suggestion (✨ dashed chips, dismiss-on-click, manual picks persisted), per-skill description panel on chip click; Settings → Skill Library CRUD with keywords field.
- Fixed the root cause of single-file outputs: `ExportCodeBlocks` now splits fence-less `// filepath:` annotated output into separate files (12-file project verified vs. 1 file before).

### 14. 📦 Model Catalog Sync & Limits
- `GET /api/agents/models/available` + `POST /api/agents/models/sync` pull the provider's OpenAI-compatible `/v1/models` catalog (NVIDIA: ~119 models); "Sync from NVIDIA" button in Settings imported 117 models.
- Agent max token cap raised 32,768 → 200,000; Coder set to 16,384, Reviewer to 8,192 (NIM accepts ≥65,536 for llama-3.1-8b, verified).
- Model recommendations benchmarked live and documented in README (Coder: qwen3.5-122b-a10b; Planner: gpt-oss-120b; Reviewer: deepseek-v4-flash; Research: nemotron-3-super-120b-a12b; Ops: llama-3.1-8b), including an avoid-list (404 models, reasoning-only outputs, 60s+ queue latency). *(Historical — superseded by §20 on 2026-07-20: qwen3.5-122b was removed from the catalog; current chains live in README + in-app Docs.)*

### 15. 🔀 Model Fallback Chains
- `AgentDefinition.FallbackModels` (comma-separated, up to 2 fallbacks) + "Fallback Model 1/2" dropdowns on the Agents page.
- The orchestrator walks the chain (`BuildModelChain`) on `ProviderException`: every error except 401 Unauthorized advances to the next model (`ShouldFallbackToNextModel`) — timeouts, rate limits, 404/InvalidModel, provider errors. A "falling back" console event is emitted and the model call log tracks the model actually used.
- Root cause fixed: a Coder run had burned 3×120s retries on the same queued model (qwen3.5-122b free-tier congestion) and failed; now the second family takes over.
- Applied chains (primary → fb1 → fb2): Planner `gpt-oss-120b → nemotron-3-super-120b → llama-3.1-8b`; Research `nemotron-3-super-120b → step-3.7-flash → llama-3.1-8b`; Coder `qwen3.5-122b → deepseek-v4-flash → gpt-oss-120b` (timeout 300s); Reviewer `deepseek-v4-flash → qwen3.5-122b → gpt-oss-120b` (timeout 180s); Ops `llama-3.1-8b → step-3.7-flash → minimax-m3`. Retry count set to 1 per model. *(Historical — Coder/Reviewer chains superseded by §20 on 2026-07-20.)*
- Verified live: Ops primary deliberately set to a 404 model → "falling back to meta/llama-3.1-8b-instruct" event → task Completed.

### 16. 🗄️ Real EF Migration (goodbye startup DDL)
- New `CredentialsSkillsAndFallbacks` migration formalizes everything the guarded startup SQL used to patch: agent columns (Provider, CustomApiUrl/Key, ApiCredentialId, FallbackModels), `api_credentials` and `skill_definitions` tables, indexes, and the FK. Written as **idempotent SQL** (IF NOT EXISTS / conditional FK) so it applies cleanly to fresh databases *and* databases already patched at runtime; scaffolded `UpdateData` ops were removed so live data (agent model chains) survives.
- `PendingModelChangesWarning` suppression removed — snapshot now matches the model, future drift fails loudly.
- Startup `Ensure*` methods slimmed to **data seeding only** (credentials, skills upsert, models, recommended chains); all DDL deleted, including the legacy index drop that used to undo the Type index every boot.
- Added `DesignTimeDbContextFactory` + local `dotnet-ef` tool manifest for future migrations.
- Verified: live DB migrated with all data intact (chains/skills/credentials), and a fresh database built purely from migrations came up with 20 skills, 11 models, 6 credentials, and the recommended agent chains auto-applied.

### 17. 🐳 Housekeeping
- Docker Compose project renamed `nvidia-agent-console` → `omni-agent-console` (pinned via `name:` in compose); Postgres data migrated to the new volume, old images removed.
- README rewritten to match the current architecture.

---

## 🗓️ 2026-07-20 — Agentic Tool Loop (Claude Code session)

### 18. 📁 Unnamed Export Files → `output/` Subfolder
- Fallback-named code blocks (no first-line filepath comment, no filename in the preceding text) used to land as `output_N.txt` in the workspace **root**; they now go to an `output/` subfolder (`ExportCodeBlocks` fallback path). Existing stray files in `workspace/postgresql/` were moved there.
- New unit test `FencedBlocks_WithoutFilename_FallBackToOutputFolder`.

### 19. 🤖 Agentic Tool Loop — the Coder now works like Claude Code
- **Why**: the one-shot pipeline required the Coder to emit an entire multi-file project in a single completion. On congested free-tier endpoints the run burned its 300s budget on a README and never reached the code (verified on a failed "linklet" task). Fence-scraping + filename guessing was the wrong architecture.
- **Provider layer**: `ChatMessage` gained `ToolCalls`/`ToolCallId`, new `ChatToolCall` + `ToolDefinition` records, `ModelRequest.Tools`, `ModelResponse.ToolCalls`. `OmniAgentModelProvider` serializes OpenAI-compatible `tools`/`tool_choice` and parses `message.tool_calls` (object or string `arguments`).
- **Tools**: new `AgentWorkspaceTools` (Application layer) exposes `write_file`, `read_file`, `list_files`. Every path goes through `WorkspacePathGuard`; limits: 50 files/task, 1M chars/file, 24k chars/read, 200 list entries. Failures return messages to the model (self-correction) instead of throwing.
- **Loop** (`RunCoderToolLoopAsync`): up to **24 iterations**; each iteration is one short chat completion logged as its own `ModelCallLog` (per-iteration usage/cost). Tool calls stream to the console as `🔧 Wrote app/main.py (886 chars)` events. When the model answers without tool calls, that answer is the final summary; the written-file list is appended for the Reviewer.
- **Sticky model**: each iteration's chain starts from the model that last succeeded, so a dead primary is not re-tried 24 times.
- **Graceful finish**: if the whole model chain fails mid-loop but files were already written, the run finishes as Completed with a warning instead of failing the task and discarding the work.
- **Fallback**: a model that never calls tools (no function-calling support) still works — its final answer goes through the legacy fence/`filepath:` exporter.
- **Prompts**: seeded Coder system prompt + role instruction rewritten for tool usage, including "you cannot execute code — do not write scratch/check scripts" (a live run showed deepseek repeatedly writing `_run_check.py` trying to verify itself).

### 20. 📉 Model Catalog Reality Check
- `qwen/qwen3.5-122b-a10b` (previous Coder primary) was **removed from the NIM catalog — endpoint returns HTTP 410 Gone**. HTTP 410 now maps to `InvalidModel` (no pointless transient retries).
- New chains (DB + seed + README + docs): Coder `deepseek-v4-flash → gpt-oss-120b → nemotron-3-super-120b-a12b` (300s); Reviewer `gpt-oss-120b → nemotron-3-super-120b-a12b → deepseek-v4-flash` (180s) — Reviewer deliberately stays a different family than the Coder.

### 21. 🛠️ Review Findings Hardening (same session)
- **Cross-process cancel**: new `ITaskCancellationBroadcast` — API publishes cancels to Redis (`task-cancellations` channel); worker-side `RedisTaskCancelSubscriber` cancels the local token, aborting the in-flight HTTP call. DB status is written *before* the token fires so the worker classifies it as a user cancel (ACK, no requeue). The Coder tool loop additionally re-checks task status from the DB every iteration as a belt-and-braces fallback.
- **API startup recovery race fixed**: `RecoverInterruptedTaskRunsAsync` now runs only in single-process mode (no RabbitMQ). With a separate worker, an API restart no longer marks live Running tasks as Failed — dead runs are covered by queue NACK/redelivery.
- **Poison-message guard**: `QueueMessage` carries the broker's `Redelivered` flag; a redelivered message that fails unexpectedly again is finalized as Failed and ACK'ed instead of requeueing forever (max 2 delivery attempts).
- **Infra ports bound to loopback**: Postgres/Redis/RabbitMQ/Vault/OpenSearch host ports default to `127.0.0.1` via `INFRA_BIND_ADDRESS` (documented in `.env.example`).
- **InputSanitizer widened**: Anthropic `sk-ant-`, Google `AIza`, GitHub `ghp_`/`github_pat_`, Slack `xox*`, JWTs, and key=value/JSON assignments (`PASSWORD=`, `JWT_SECRET=`, `"api_key": "..."`) — value-only redaction keeps keys readable; prose about secrets is left untouched (tested).
- **Reasoning-content fallback**: when `content` is empty, the provider now reads `reasoning_content`/`reasoning` — reasoning-only Nemotron variants produce usable output instead of "empty response" failures.

### 22. 🏫 Dual Deployment Profiles — Shared-Lab Tenant MVP
- **Decision**: not "laptop OR shared-server" but both — same codebase, profile chosen by `SHARED_LAB` env (default `false` = today's single-user behavior, byte-for-byte).
- **Shared-lab mode** (`SHARED_LAB=true`): browser-generated session id (`X-Studio-Session-Id` header / `session_id` query for WebSockets); `TaskRun.OwnerSessionId` (migration `SharedLabTaskOwnership`) with owner-filtered task endpoints (foreign task → 404, no info leak, including ConsoleHub SubscribeTask); workspace confined per session via effective guard root `/workspace/sessions/{id}/` (task-create rewrites workspacePath; WorkspaceController scopes tree/read/delete); config writes (credentials/agents/skills/settings/providers) 403 for students with `/api/skills/suggest` exempt; console API key = instructor credential.
- **Fail-fast**: `SHARED_LAB=true` without `CONSOLE_API_KEY` refuses to start — anonymous shared deployment impossible by mistake.
- **Verified live, both paths**: flag off = legacy behavior (200/201 without headers); flag on = 400 without session, A/B isolation (404s, empty list), workspacePath mapped in DB, 403 on credential write, suggest open, admin sees all; fail-fast confirmed (API not serving).
- New `SharedLabPolicy` (pure, unit-tested: id charset, path-map idempotency, foreign-prefix rejection, admin-gate matrix, startup guard) + frontend session identity (localStorage + interceptor + hub query).

### 23. 🧩 Orchestrator Refactor (Sprint B, R1–R4)
- `AgentOrchestratorService` split behavior-preservingly from **1442 → 487 lines**, one commit per slice: `CodeBlockExporter` (legacy markdown export), `ModelChainExecutor` (chain walk/retry/fallback events; keeps the public static test surface), `AgentPromptBuilder` + `RunTelemetry` (messages/metadata/context vs. payloads/cost/hash/trims), `CoderToolLoopRunner` (the agentic tool loop as a composition of the above).
- Target architecture now matches docs/ROADMAP.md §2.4: RunTaskAsync is a thin coordinator — Coder+workspace → CoderToolLoopRunner, else RunAgentAsync → ModelChainExecutor.
- 113 tests green with unchanged asserts (export/fallback tests retargeted); live tool-loop smoke on the deployed stack.

---

## 📈 Verification Status

- **Unit Tests**: 113 tests build and pass (path guard, export splitting, workspace tools, requeue decision, skill suggestion, model fallback chain, sanitizer incl. widened patterns, shared-lab policy, token usage).
- **Frontend Compiler**: Angular 21 builds successfully (only pre-existing SCSS budget warnings).
- **Docker Compose**: All containers (Postgres, RabbitMQ, Redis, Vault, API, Frontend, Worker) built and running under the `omni-agent-console` project name.
- **Live E2E**: masked credentials round-trip, path traversal 400s, shutdown NACK/requeue + user-cancel ACK, 12-file project export with skills, model catalog sync, prompt-based skill auto-suggestion, model fallback chain (404 primary → fallback completed the run) — all verified against the running stack.
- **Live E2E (tool loop, 2026-07-20)**: FastAPI notes-API task → Coder (deepseek-v4-flash) wrote 6 files via `write_file` in iteration 1 (including `tests/` subfolder), mid-run deepseek outage fell back to gpt-oss-120b, task **Completed** with all files on disk and 🔧 events streamed live.
