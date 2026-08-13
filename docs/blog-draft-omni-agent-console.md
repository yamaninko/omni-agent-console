# OmniAgent Console: multi-agent coding + moderated AI debates in one Docker stack

*Draft for Dev.to / LinkedIn / personal blog — paste, edit voice, publish, link the repo.*

---

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

---

*Tags: multi-agent, llm, dotnet, angular, docker, nvidia, openai*
