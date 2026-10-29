# 02 Stale and error states

Status: ready-for-agent
Type: task
Blocked by: 01

## What to build

When an Update fails, keep showing the last known Usage in grey (**Stale**) and explain why on hover. See [spec.md](../spec.md) section "Stale and errors".

- No credentials file: "Sign in to Claude Code first".
- Token expired (`expiresAt` passed) or HTTP 401: "Open Claude Code to sign in again". Never refresh the token.
- Network error, timeout or other HTTP error: "Offline, last updated HH:mm".
- Retry only on the normal 5-minute tick or on click. No retry loops.
- Hover tooltip in the normal state: Weekly limit Reset day and time (`Mon 09:00`) and the time of the last Update.

## Acceptance criteria

- Renaming the credentials file turns the widget grey with the right hint and doesn't crash it. Renaming it back recovers on the next Update.
- Unplugging the network shows "Offline" with the last Update time, and the last % stays visible.
- Unit tests cover mapping each failure to its hint.

## Comments
