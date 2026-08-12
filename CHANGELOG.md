# Changelog

All notable changes to OmniAgent Console are documented here.

Format roughly follows [Keep a Changelog](https://keepachangelog.com/).  
Versions are date-based until a formal semver release.

---

## [Unreleased]

### Planned (see [docs/ROADMAP.md](docs/ROADMAP.md) § Panel backlog)

- LLM-driven moderator floor selection
- Shared-lab student-only shell
- Studio pipeline picker (partial agent chain)

### Done on this branch (post-MVP, local commits)

- **T1** Vault/key bootstrap from `OMNIAGENT_API_KEY` on API startup; Panel banner when key missing.

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
