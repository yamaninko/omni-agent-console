# OmniAgent Console v0.1.0

First public milestone of the multi-agent studio.

## Highlights

- **Studio** — Planner → Research → Coder → Reviewer → Ops with optional fix loop  
- **Coder tool loop** — `write_file` / `read_file` / `list_files` into `workspace/`  
- **Panel debates** — Agent Groups, roles/stances, multi-round floor, continue, votes, transcripts  
- **Skills + model registry** — NVIDIA NIM catalog sync, fallback chains, estimated cost  
- **Ops** — RabbitMQ at-least-once, Redis cancel/console, Vault + durable secret mirror  
- **Shared-lab** — session isolation, student nav, instructor key  
- **Docker Compose** one-command local stack  

## Quick start

```bash
git clone https://github.com/yamaninko/omni-agent-console.git
cd omni-agent-console
cp .env.example .env   # set OMNIAGENT_API_KEY
docker compose up -d --build
open http://localhost:4210
```

## Links

- README: repository root  
- Changelog: [CHANGELOG.md](../CHANGELOG.md)  
- Roadmap: [ROADMAP.md](ROADMAP.md)  
- Security: [SECURITY.md](../SECURITY.md)  

## Tag

Create with:

```bash
git tag -a v0.1.0 -m "v0.1.0 first public milestone"
git push origin v0.1.0
gh release create v0.1.0 -F docs/RELEASE_NOTES_v0.1.0.md
```
