# Changelog

All notable changes to OmniAgent Console are documented here.

Format roughly follows [Keep a Changelog](https://keepachangelog.com/).  
Versions are date-based until a formal semver release.

---

## [Unreleased]

### Planned (see [docs/ROADMAP.md](docs/ROADMAP.md) § Panel backlog)

- Full multi-turn LLM floor negotiation (beyond heuristic / parse mode)
- Playwright E2E in CI
- Audience mid-round inject / STT / full i18n

### Done on this branch (post-MVP, local commits)

- **N1** Instructor **live sessions** list on Dashboard (tasks + panels, session id, cost)
- **N2** Playwright **smoke** suite (`frontend/e2e`, optional workflow) + `scripts/set-github-about.sh`
- **N3** Annotated tag **v0.1.0** (release notes already in `docs/RELEASE_NOTES_v0.1.0.md`)
- **W1** SEO assets, SECURITY/CONTRIBUTING, release notes, blog draft, CI unit tests, good-first-issue template
- **W2** Demo seed (`POST /api/demo/seed-debate`), Studio presets, panel scorecard, workspace smoke, export ZIP
- **W3** Student home banner, dashboard cost + live counts, shared-lab quotas, panel ZIP hand-in
- **W4** Panel floor mode (`Panel:FloorMode` / `PANEL_FLOOR_MODE` = fixed|llm), queue fairness (panel priority)
- **SEO1** README landing (value prop, Features, Quick start, Use cases, TR özet) + MIT `LICENSE` + `docs/GITHUB_SEO.md`
- **F1** Panel **audience vote** (“Who convinced you?”) — `POST /api/panels/{id}/vote`, `VotesJson` jsonb + tallies on detail.
- **F2** Groups **template gallery** — one-click 3-for/1-against and 2v2 cast presets.
- **F3** Studio **pipeline picker** — `full` | `coder` | `plan-code-review` via `InputContextJson.pipeline` + `TaskPipelinePolicy`.
- **F4** Task **est. cost** card on detail + cost column on History (sum of model call estimates).
- **F5** Shared-lab **student nav shell** — settings exposes `sharedLabEnabled` / `isAdmin`; students hide Agents / Dashboard / Settings.

- **T1** Vault/key bootstrap from `OMNIAGENT_API_KEY` on API startup; Panel banner when key missing.
- **T2** Unified History page: Studio tasks + panel sessions with GUID deep links.
- **T3** Panel multi-round (1–3) + user follow-up `POST /continue` (extra roster pass).
- **T4** Panel Markdown transcript export (`GET /panels/{id}/transcript` + Export .md).
- **T5** Group clone API/UI + “Open in Panel” deep link (`?groupId=`).
- **UI polish** Nav grouped into Build / Debate / Ops; shared design tokens + `.oa-*` kit (no full rebrand).
- **Themes** Dark / Blue / White switcher (sidebar, `localStorage`); Panel chat typography (line-height, spacing, bubble contrast).
- **P1** Panel stream **Conversation / All events** filter (hides model noise by default).
- **P2** Durable secret mirror: `./data/secrets` volume + `FileSecretStore` behind Vault (survives `-dev` wipe).
- **P3** Panel **sample topic chips** (group-aware: Anunnaki / remote / default).
- **P4** **Home** page: quick links, recent activity, first-run checklist (default route).
- **N1** Panel **auto-scroll** + **is speaking…** bar (floor timer); poll refreshes events mid-run.
- **N2** Home **API key badge** (configured / not + masked preview when available).
- **N3** `scripts/smoke-panel.sh` live smoke (group → panel → ≥1 completed turn).
- **N4** In-app Docs: **Moderated Panel (Debate)** how-to section.
- **D1** Floor **progress bar** on speaking bar (ticks every second).
- **D2** **Delete panel** session (`DELETE /api/panels/{id}` + UI).
- **D3** `make smoke` / `make smoke-long` helpers.
- **D4** Angular component style budget raised (12kB/20kB) to clear kit warnings.
- **D5** `docs/PR_BODY.md` for local PR/push readiness (still no push).
- **E1** Panel **queue/worker-busy** hint while Pending.
- **E2** Browser **TTS Read** on speeches (+ Stop TTS).
- **E3** Roster briefing **collapsed by default** (Expand/Collapse).
- **E4** **Clear finished** bulk delete (`POST /api/panels/bulk-delete`).

---

## [2026-08-12] — Moderated Panel & Agent Groups

### Added

- **Agent Groups** (`/groups`, `/groups/{guid}`): named collections of panel personas independent of Studio pipeline `AgentDefinition`s.
- **Panel personas** with **Role** (`Moderator` | `Commentator`), **Stance** (`Neutral` | `For` | `Against` | `Custom`), optional **stance label**, model/fallback/timeout, and system prompt (who they are).
- **Moderated Panel** (`/panel`, `/panel/{sessionGuid}`): user topic → automatic floor order (moderators first) → single-round speeches (~60s generation budget) → SignalR live stream.
- **Roster briefing** before anyone speaks: console card lists each speaker’s job, persona blurb, and stance so models do not invent co-panelists.
- **Queue kind** `panel-session` on the shared RabbitMQ / in-memory work queue (worker dispatches Studio tasks vs panel sessions).
- EF migrations: `AgentGroupsAndPanelSessions`, `PanelMemberRoleAndStance`.
- Deep links and GUID display for groups, panels, and task history IDs.
- Settings: saving OmniAgent API key also re-seeds the default NVIDIA credential Vault path (survives partial key loss after Vault wipe).
- Panel start preflight: clear 400 if no provider API key is configured.
- Credential resolution fallback: empty per-credential Vault path → `providers/omniagent` / `OMNIAGENT_API_KEY`.

### Fixed

- Vault **256M** OOM (exit 137) on cold start → default **512M** (`VAULT_MEM_LIMIT`).
- Panel turns without `ApiCredentialId` ignored the default NIM credential and reported `API key is not configured` even when Studio credentials existed.

### Changed

- Docker Compose vault memory limit; shared-lab admin-gates `/api/agent-groups` writes.
- Nav: **Groups**, **Panel**.

### Docs

- README: Groups & Panel section.
- This changelog.
- ROADMAP: completed panel MVP + prioritized backlog (T1–T5 and later).

---

## [2026-08-12] — README English

### Changed

- Full README translation from Turkish to English (setup, architecture, model chains preserved).

---

## Earlier (2026-07)

See [docs/ROADMAP.md](docs/ROADMAP.md) and [AGENT.md](AGENT.md) for dual deployment, orchestrator refactor, tool loop, Vault secret-refs, frontend Vitest, Reviewer fix loop, and related work.
