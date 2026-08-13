# Publish checklist (post v0.1.0)

## Already done in repo
- [x] Public `main` + tag **v0.1.0** + GitHub Release
- [x] README landing, MIT LICENSE, SECURITY, CONTRIBUTING
- [x] Blog draft: [blog-draft-omni-agent-console.md](blog-draft-omni-agent-console.md)
- [x] SEO helper: [GITHUB_SEO.md](GITHUB_SEO.md) + `scripts/set-github-about.sh`

## Do once on GitHub (web)
1. **About** description + **Topics** (script may 403 on narrow tokens — paste from GITHUB_SEO.md)
2. Pin repo on profile
3. Optional: repo variable `E2E_BASE_URL` for scheduled Playwright workflow

## Publish blog (copy-paste)
1. Open [blog-draft-omni-agent-console.md](blog-draft-omni-agent-console.md)
2. Post on Dev.to / LinkedIn / Medium with link:  
   https://github.com/yamaninko/omni-agent-console
3. Cross-link from GitHub README “Community” if you want

## Local smoke after changes
```bash
docker compose up -d --build
cd frontend && npx playwright install chromium && npm run test:e2e
curl -s http://localhost:5080/api/settings | head -c 200
```

## LLM floor
```bash
# .env
PANEL_FLOOR_MODE=llm
docker compose up -d --build agent-worker backend-api
```
