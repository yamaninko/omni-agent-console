#!/usr/bin/env bash
# Sets repo About description + topics via GitHub API.
# Requires: gh auth login   OR   GH_TOKEN / GITHUB_TOKEN
set -euo pipefail

REPO="${1:-yamaninko/omni-agent-console}"
DESC='Multi-agent AI studio: coding pipeline + moderated debate panels. .NET 10, Angular 21, Docker Compose, NVIDIA NIM / OpenAI-compatible LLMs.'
TOPICS='["multi-agent","llm","ai-agents","dotnet","aspnetcore","angular","nvidia","openai-api","docker","docker-compose","agent-orchestration","debate","panel-discussion","signalr","rabbitmq"]'

if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
  gh repo edit "$REPO" --description "$DESC"
  # topics: replace via API (gh repo edit --add-topic is additive per topic)
  for t in multi-agent llm ai-agents dotnet aspnetcore angular nvidia openai-api docker docker-compose agent-orchestration debate panel-discussion signalr rabbitmq; do
    gh repo edit "$REPO" --add-topic "$t" 2>/dev/null || true
  done
  echo "OK via gh: $REPO"
  exit 0
fi

TOKEN="${GH_TOKEN:-${GITHUB_TOKEN:-}}"
if [[ -z "$TOKEN" ]]; then
  echo "No gh auth and no GH_TOKEN/GITHUB_TOKEN. Run: gh auth login" >&2
  exit 1
fi

curl -fsS -X PATCH \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/vnd.github+json" \
  "https://api.github.com/repos/$REPO" \
  -d "{\"description\":$(python3 -c 'import json,sys; print(json.dumps(sys.argv[1]))' "$DESC")}"

curl -fsS -X PUT \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/vnd.github+json" \
  "https://api.github.com/repos/$REPO/topics" \
  -d "{\"names\":$TOPICS}"

echo "OK via token: $REPO"
