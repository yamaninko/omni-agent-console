# Browser agent playbook — OmniAgent Console (manual GitHub / blog / E2E)

**Audience:** Chrome Code (or any browser automation agent).  
**Repo:** https://github.com/yamaninko/omni-agent-console  
**Owner:** user is already logged into GitHub (and Dev.to / LinkedIn if posting).

Do tasks **in order A → B → C**. After each task, report: URL, success/fail, screenshot note.

---

## TASK A — GitHub About + Topics (required, ~2 min)

### Goal
Set repository description and topics so the repo is discoverable on GitHub Search and Google.

### Steps

1. Open: `https://github.com/yamaninko/omni-agent-console`
2. Confirm the repo is **Public** (About sidebar or Settings → General → Danger Zone). If Private, switch to Public only if the user wants that (default: leave Public).
3. On the repo home page, right sidebar **About**, click the **gear** icon (Edit repository details).
4. **Description** — clear existing text and paste **exactly**:

```text
Multi-agent AI studio: coding pipeline + moderated debate panels. .NET 10, Angular 21, Docker Compose, NVIDIA NIM / OpenAI-compatible LLMs.
```

5. **Website** — leave empty **unless** a public demo URL exists. If empty, do not invent one.
6. **Topics** — add each of the following (type + Enter / Add), avoid duplicates:

```text
multi-agent
llm
ai-agents
dotnet
aspnetcore
angular
nvidia
openai-api
docker
docker-compose
agent-orchestration
debate
panel-discussion
signalr
rabbitmq
```

7. Check **Include in the home page** if present (optional, preferred on).
8. Click **Save changes**.
9. Verify on the repo page: description shows under the repo name; topics chips are visible.
10. Optional: open `https://github.com/yamaninko?tab=repositories` and **Pin** `omni-agent-console` if not already pinned (Profile → Customize pins).

### Success criteria
- [ ] Description matches the paste text  
- [ ] All 14 topics present  
- [ ] Repo public  

### Fail if
- Not logged in as owner of `yamaninko`  
- Gear icon missing → use Settings → General → “Repository name / description” fields  

---

## TASK B — GitHub Actions variable for hosted E2E (optional)

### Goal
Enable optional Playwright workflow when a **public HTTPS preview** of the UI exists.

### Skip condition
If there is **no public URL** for the Angular UI (only `localhost:4210`), **skip Task B** and write: `SKIPPED: no public E2E_BASE_URL`.

### Steps (only if public UI URL exists)

1. Open: `https://github.com/yamaninko/omni-agent-console/settings/variables/actions`
2. Under **Repository variables**, click **New repository variable**.
3. **Name** (exact):

```text
E2E_BASE_URL
```

4. **Value** — the public UI origin only, no trailing slash, e.g.:

```text
https://console.example.com
```

   (Use the real URL the user provides. Do not invent.)

5. Click **Add variable**.
6. Open: `https://github.com/yamaninko/omni-agent-console/actions/workflows/e2e-smoke.yml`
7. Click **Run workflow** → branch `main` → **Run workflow**.
8. Confirm the run starts. If it is skipped because variable was empty, re-check name spelling.

### Success criteria
- [ ] Variable `E2E_BASE_URL` exists  
- [ ] Workflow can be dispatched (or next schedule will use it)  

### Notes for agent
- Workflow file: `.github/workflows/e2e-smoke.yml` (only runs on `workflow_dispatch` / weekly schedule when variable is set).
- Local equivalent (no GitHub): user runs `make smoke-e2e E2E_BASE_URL=https://…` on their machine.

---

## TASK C — Publish blog post (Dev.to preferred; LinkedIn optional)

### Goal
Publish a short post linking the GitHub repo for SEO backlinks.

### C1 — Dev.to (preferred)

1. Open: `https://dev.to/new` (login if needed).
2. **Title:**

```text
OmniAgent Console: multi-agent coding + moderated AI debates in one Docker stack
```

3. **Body** — paste Markdown:

```markdown
Building with LLMs is no longer a single chat box. You want a **pipeline of specialists** (plan, research, code, review) and sometimes a **room full of personas** arguing a topic with clear roles.

**OmniAgent Console** is an open-source web console that does both:

1. **Studio** — multi-agent coding pipeline (.NET 10 worker, Angular UI, workspace files, skill packs, model fallbacks).  
2. **Panel** — moderated debates: moderator + For/Against commentators, floor timer, live stream, audience votes, Markdown transcripts.

Provider surface is **OpenAI-compatible** (NVIDIA NIM by default). One `docker compose up` brings Postgres, Redis, RabbitMQ, Vault, API, worker, and frontend.

### Why not “just ChatGPT”?

- Agents have **different models and timeouts**.  
- The Coder **writes files with tools**, not only markdown fences.  
- Debates have a **roster** so models stop inventing guests.  
- Classroom mode (`SHARED_LAB`) isolates student sessions.

### Try it

Repo: https://github.com/yamaninko/omni-agent-console  

```bash
cp .env.example .env   # OMNIAGENT_API_KEY=...
docker compose up -d --build
# UI http://localhost:4210
```

If you teach agents, run product debates, or want a local NIM-friendly studio, star the repo and open an issue with your stack.

Latest release: https://github.com/yamaninko/omni-agent-console/releases/tag/v0.2.0
```

4. **Tags** (Dev.to allows up to 4 typically):

```text
dotnet
angular
docker
ai
```

   If more tags allowed, also: `llm`, `opensource`.

5. **Canonical URL** (if field exists): leave empty or set to the GitHub repo URL.
6. Click **Publish** (not only Save draft), unless user prefers draft — default **Publish**.
7. Copy the published post URL.

### C2 — LinkedIn (optional, after Dev.to)

1. Open: `https://www.linkedin.com/feed/`
2. **Start a post**.
3. Paste **plain text** (LinkedIn is not full Markdown):

```text
Open-sourced OmniAgent Console — multi-agent coding studio + moderated AI debate panels.

• Studio: Planner → Coder tool-loop → Reviewer (.NET 10 + Angular 21 + Docker)
• Panel: multi-persona debates with floor timer, votes, transcripts
• OpenAI-compatible APIs (NVIDIA NIM by default)
• Shared-lab mode for classrooms

GitHub: https://github.com/yamaninko/omni-agent-console
Release: https://github.com/yamaninko/omni-agent-console/releases/tag/v0.2.0

#dotnet #angular #docker #AI #opensource
```

4. **Post** (public).
5. If Dev.to URL exists, add it as a comment or first line: `Write-up: <dev.to-url>`.

### C3 — Wire blog URL back into GitHub About (if published)

1. Return to: `https://github.com/yamaninko/omni-agent-console` → About gear.
2. **Website** field → paste the Dev.to (or LinkedIn) public URL.
3. Save.

### Success criteria
- [ ] At least one public post with the GitHub link  
- [ ] Post URL reported to the user  
- [ ] Optional: Website field on GitHub About updated  

---

## TASK D — Google indexing nudge (optional, 1 min)

1. Open Google (logged in optional):  
   `https://www.google.com/search?q=site%3Agithub.com%2Fyamaninko%2Fomni-agent-console`
2. Note whether the repo appears.
3. If **Google Search Console** is available for the user and they can add a URL property:
   - Property: `https://github.com/yamaninko/omni-agent-console`
   - URL Inspection → `https://github.com/yamaninko/omni-agent-console` → **Request indexing**  
   (Often limited for github.com; do not fail the whole playbook if GSC rejects.)

---

## Final report template (agent must fill)

```text
TASK A About+Topics: SUCCESS | FAIL | SKIP — notes: …
TASK B E2E_BASE_URL: SUCCESS | FAIL | SKIP — value: … / reason: …
TASK C Blog: SUCCESS | FAIL | SKIP — platform: … URL: …
TASK C3 Website field: SUCCESS | FAIL | SKIP
TASK D site: search: INDEXED | NOT_SEEN | SKIP
```

---

## Absolute constraints for the agent

- Do **not** change repository code, secrets, or delete the repo.
- Do **not** invent a public demo URL for Website or `E2E_BASE_URL`.
- Do **not** post private API keys or `.env` contents.
- Prefer **Publish** only on content the user already approved (this playbook is the approval).
- If 2FA or CAPTCHA blocks you, stop and report `BLOCKED: human 2FA/CAPTCHA`.
