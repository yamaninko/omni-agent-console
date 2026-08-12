# Local stack: Panel / Groups / UX (not pushed)

**Branch tip:** `main` (local) — do **not** push until reviewed.  
**Base:** `origin/main`

## Summary

Delivers the moderated **Panel** product surface (agent groups, stances, roster, multi-round, continue, transcript), durable lab secrets, Home dashboard, themes, and UX polish for debate streams — without changing the Studio coding pipeline.

## Commits (oldest → newest)

See `git log origin/main..HEAD --oneline`.

Highlights:

1. Panel + Groups MVP (roles, stances, queue kind, SignalR, deep links)
2. Docs: CHANGELOG, README Panel section, ROADMAP T1–T5
3. T1 key bootstrap · T2 unified History · T3 multi-round/continue · T4–T5 export/clone
4. UI nav groups + design kit · dark/blue/white themes
5. Conversation filter · file secret mirror · topic chips · Home
6. Speaking bar · key badge · smoke script · Panel docs
7. Floor progress · panel delete · `make smoke` · SCSS budgets
8. Queue-busy hint · TTS Read · collapsed roster · bulk-delete finished

## Test plan

- [x] `dotnet test` backend unit tests
- [x] `cd frontend && npm test`
- [x] `cd frontend && npm run build`
- [ ] `docker compose up -d --build`
- [ ] Settings API key → Home key badge green
- [ ] Panel: sample topic → Start → speaking bar + progress → Export .md
- [ ] `make smoke` (or `BASE=http://localhost:5080 make smoke`)
- [ ] Delete finished panel from Saved list
- [ ] Themes: Dark / Blue / White
- [ ] Studio regression: create task still works

## Notes

- Secrets: Vault + mirror under `./data/secrets` (gitignored).
- Free-tier model latency may leave smoke in Running; script PASSes with ≥1 completed turn.
- **Push only after explicit approval.**
