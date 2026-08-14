# Changelog

All notable changes to **OmniAgent Console** are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/).  
Versions use date-based tags until formal semver; GitHub release **v0.1.0** marks the first public milestone.

---

## [Unreleased]

### Added

#### Workspace — add / copy existing projects
- **Add project** panel on Workspace explorer (open by default)
- **Copy from host**: list folders under `HOST_IMPORT_DIR` (compose mount `/host-import`) → server-side copy into `./workspace/{name}`
- **Browser folder upload**: webkitdirectory import → multipart copy into workspace
- **Empty folder** create (`POST /api/workspace/folders`)
- Limits & safety: max ~500 files / 2 MiB each / 50 MiB total; skip `node_modules`, `.git`, `dist`, …
- Env: `HOST_IMPORT_DIR`, `WORKSPACE_IMPORT_ENABLED` (see `.env.example`)

#### Studio — bind existing project
- Left-sidebar **project list** (always visible): pick folder → bind path + edit-in-place mode
- Task context `existingProject: true`; agents list/read before write (no greenfield rebuild)
- Workspace **Open in Studio** always available (not only after failed docker start)
- Path row + dropdown backup picker; layout fixes (no overlapping console / folder strip)

### Planned

- Hosted Playwright via GitHub `vars.E2E_BASE_URL` (public UI URL required)
- Full in-app Docs body translation (headers already i18n)
- GitHub About **Website** field (blog URL when published; PAT may lack metadata scope)

---

## [0.2.1] — 2026-08-13

### Added

- Dependabot weekly updates (GitHub Actions, frontend npm, backend NuGet)
- Good first issues for Studio i18n leftovers and Panel floor docs
- Browser agent playbook for About/Topics, E2E var, blog publish

### Fixed

- README stack line rendered as a broken markdown table

### Docs

- README restructure; CHANGELOG Keep a Changelog layout for 0.2.0 / 0.1.0
- i18n headers for Dashboard, Workspace, Agents, Docs

---

## [0.2.0] — 2026-08-13

Product surface after the panel MVP: demos, lab UX, i18n, sandbox tools, SEO packaging.

### Added

#### Studio
- Pipeline picker: `full` | `coder` | `plan-code-review` (`TaskPipelinePolicy`)
- Demo presets (FastAPI / .NET / Angular) + Home sample Studio links
- Soft cost budget `maxCostUsd` (stop remaining agents when spent)
- Coder tool **`run_terminal`** (whitelist: pytest, npm test, dotnet test, go test, ruff, tsc)
- Workspace smoke summary after task complete (file count / compose / README)

#### Panel & Groups
- Audience vote (“Who convinced you?”) + tallies
- Audience **inject** mid-run (`POST /api/panels/{id}/inject`)
- Score card on complete + **export ZIP** (transcript + meta + scorecard)
- LLM floor order (`PANEL_FLOOR_MODE=llm`) with heuristic fallback
- Group **IsTemplate** (instructor library; students **clone**)
- Cast template gallery (3-for/1-against, 2v2)
- Browser **STT** mic + language chips (EN/TR/DE/FR/ES); TTS Read (prior)
- Multi-round, continue, transcript MD, delete / bulk-delete, floor bar, queue hint

#### Shared lab & ops
- Student nav shell (`sharedLabEnabled` / `isAdmin`)
- Student **quota card** on Home (concurrent / daily tasks / tokens)
- Session quotas on task create
- Dashboard: estimated cost, live session counts, **live list + Cancel**
- Panel-first queue fairness (in-memory dual channel)

#### Platform & docs
- Home demo seed (`POST /api/demo/seed-debate`)
- EN/TR shell + page headers (Home, Studio, Panel, Groups, History, Settings, Dashboard, Workspace, Agents, Docs)
- Playwright smoke suite + `make smoke-e2e` (system Chrome)
- CI: backend tests + frontend unit tests
- MIT LICENSE, SECURITY.md, CONTRIBUTING.md, `docs/RELEASE_NOTES_v0.1.0.md`, `docs/GITHUB_SEO.md`, `docs/PUBLISH_CHECKLIST.md`, blog draft
- README landing (features table, quick start, use cases, screenshots)

### Changed

- README and CHANGELOG reorganized for public consumption (this release)

---

## [0.1.0] — 2026-08-12

First public milestone tag: [v0.1.0](https://github.com/yamaninko/omni-agent-console/releases/tag/v0.1.0).

### Added

- **Agent Groups** + **Moderated Panel** (roles, stances, roster briefing, SignalR, GUID deep links)
- Unified **History** (Studio tasks + panels)
- Panel multi-round + user **continue**
- Transcript export, group clone, Open in Panel
- Themes (dark / blue / white), Build/Debate/Ops nav, Home page
- Vault/key bootstrap from `OMNIAGENT_API_KEY`, durable secret mirror
- Smoke scripts (`make smoke`)

### Fixed

- Vault OOM cold-start (512M default)
- Panel credential resolution when member has no `ApiCredentialId`

---

## [2026-07] — Core studio stack

Baseline multi-agent coding console (summarized; detail in [AGENT.md](AGENT.md) and [docs/ROADMAP.md](docs/ROADMAP.md)):

- Planner → Research → Coder (tool loop) → Reviewer → Ops; optional fix loop
- RabbitMQ at-least-once, Redis cancel/console, shared-lab dual profile
- Skill library + auto-suggest, model catalog sync, fallback chains
- Workspace path guard, Vault secret-refs, Angular Vitest

---

## Links

- [README](README.md)
- [Roadmap](docs/ROADMAP.md)
- [Publish checklist](docs/PUBLISH_CHECKLIST.md)
- [GitHub releases](https://github.com/yamaninko/omni-agent-console/releases)
