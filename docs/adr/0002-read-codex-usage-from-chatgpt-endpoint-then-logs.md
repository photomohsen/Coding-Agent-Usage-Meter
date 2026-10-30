# Read Codex Usage from ChatGPT's usage endpoint, then the Codex session logs

When Codex is on the machine (`~/.codex` exists), the widget also shows Codex Session and Weekly Usage. It calls ChatGPT's undocumented `chatgpt.com/backend-api/wham/usage` endpoint with the access token and account id Codex stores in `~/.codex/auth.json`, the same read-only rule as ADR 0001: never refresh or write that token. When the call fails (token expired, 401, offline, rate limited), it shows the newest `rate_limits` entry Codex wrote to `~/.codex/sessions/**/rollout-*.jsonl` as Stale, with the time of that entry. A Window whose Reset has passed shows 0%.

## Considered Options

- **Session logs only**: no network and no token, but they only move while Codex is used, so Usage from the ChatGPT web app or another machine is missed. Kept as the fallback.
- **Run `codex` to ask**: would start a Codex process every Update. Rejected as heavy and slow.
