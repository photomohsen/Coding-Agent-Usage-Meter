# 01 Floating pill with live Usage

Status: ready-for-agent
Type: task
Blocked by: none

## What to build

The first end-to-end slice: a new .NET 10 WPF project at `src/ClaudeUsageWidget/` (not the prototype) showing the **B · Pill** look **Floating**, with real Usage. See [spec.md](../spec.md) sections Data, Look and Window.

- Project: `UseAppHost=false`, so it runs as `dotnet ClaudeUsageWidget.dll` (company app control blocks unsigned `.exe` files).
- Usage source: read the token read-only and call the usage endpoint, as in [ADR 0001](../../../docs/adr/0001-read-usage-from-oauth-endpoint-read-only.md). Keep this in its own class, separate from the UI.
- Pill: `5h 8% · 7d 67%` with thin bars, `↻ 3h 11m` countdown, colour thresholds at 75% and 90%. Solid dark card with a thin light border.
- Window: borderless, always on top, no taskbar button, not in Alt+Tab, 85% opaque and 100% on hover. Drag to move, click to Update. Starts at the top right, and its right edge stays put when its width changes.
- Update every 5 minutes and on click. Re-read the credentials file before each Update.

Reference: `prototype/UsageWidget.Prototype/` shows a working version of most of this. Rewrite it properly; don't copy it.

## Acceptance criteria

- `dotnet run --project src/ClaudeUsageWidget` shows the pill top right with the same % as `/usage`.
- The countdown ticks down live.
- It stays on top while switching between a browser, VS Code and Explorer.
- It doesn't appear in the taskbar or in Alt+Tab.
- The credentials file's modified time is unchanged after several Updates.
- Unit tests cover parsing the usage response and the countdown text.

## Comments
