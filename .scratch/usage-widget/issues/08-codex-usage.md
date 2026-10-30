# 08 Codex Usage

Status: ready-for-agent
Type: task
Blocked by: 01, 02

## What to build

When Codex is on the machine, show **Codex Usage** (Session and Weekly) under Claude's in every look. See [ADR 0002](../../../docs/adr/0002-read-codex-usage-from-chatgpt-endpoint-then-logs.md). Tried in the prototype: `CodexUsageClient.cs`.

- Update from `chatgpt.com/backend-api/wham/usage` with the token in `~/.codex/auth.json`, read-only, on the same tick as Claude.
- If that fails, show the newest `rate_limits` entry from `~/.codex/sessions` as Stale, with its time. A passed Reset shows 0%.
- Hints: no `auth.json` → "Sign in to Codex first"; expired or 401 → "Open Codex to sign in again"; network → "Offline".
- Pill: two more rows under a line, names in teal, "Codex ↻ 4h 23m". Rings: a second pair. Strip: two more columns. Docked label: "70 28 · 17 7", Claude first.
- Setting "Show Codex", on by default, only listed when Codex is found. No `~/.codex`: the widget looks as before.

## Acceptance criteria

- With Codex signed in, the Codex % matches what `codex` shows in `/status`.
- Renaming `auth.json` shows the log numbers in grey with the hint, and recovers on the next Update.
- Without `~/.codex` nothing about Codex shows, in the widget or Settings.
- A failing Codex Update never greys out Claude's Usage, and the other way round.

## Comments

- Prototype (2026-10-30): endpoint and Pill checked live (Codex 8% / 17%). The log fallback was not forced live.
