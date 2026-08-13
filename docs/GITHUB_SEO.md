# GitHub discoverability (SEO) checklist

Local guide for making **OmniAgent Console** easier to find on Google and GitHub.
Code changes land in git; **About / Topics** on github.com need the web UI or `gh`
(this machine may not have `gh` installed).

## Script (when `gh auth login` or `GH_TOKEN` has `repo` metadata scope)

```bash
chmod +x scripts/set-github-about.sh
./scripts/set-github-about.sh yamaninko/omni-agent-console
```

Note: a fine-grained or push-only token may return **403** on description/topics even if `git push` works. Use a classic PAT with `public_repo` or full `repo` scope, or set About in the web UI.

## Paste into GitHub → About (no code push required)

**Description** (≤350 chars):

```text
Multi-agent AI studio: coding pipeline + moderated debate panels. .NET 10, Angular 21, Docker Compose, NVIDIA NIM / OpenAI-compatible LLMs.
```

**Website** (optional): leave empty until a public demo exists, or set a blog post URL.

**Topics** (add all that apply):

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

## After topics/description are set

1. Confirm the repo is **Public**.
2. Google: `site:github.com/yamaninko/omni-agent-console`
3. Google Search Console → URL Inspection → request indexing for the repo root.
4. Link the repo from LinkedIn / a blog / Dev.to (backlinks help crawl).
5. Pin the repo on the GitHub profile; fill profile bio.

## README SEO notes (already applied in repo)

- H1 = product name; first paragraphs = value + keywords
- Features table + Quick start + Use cases near the top
- TR short summary for bilingual queries
- MIT `LICENSE` + badges
- Deep internal links to CHANGELOG / ROADMAP

## Do not

- Keyword-spam the README
- Expect top rankings for generic queries (“AI agent”) without backlinks
- Rely on private repos or empty About fields
