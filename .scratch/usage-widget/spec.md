# Spec: Claude Usage Widget

Status: ready-for-agent

Tickets: [issues/](issues/), in order 01–07.

A tiny always-on-top Windows widget showing the user's Claude **Session limit** (5h) and **Weekly limit** (7d) **Usage**. Terms follow [GLOSSARY.md](../../GLOSSARY.md).

## Scope

- Shows exactly two Limits: Session limit and Weekly limit, each as % Usage plus its Reset.
- Out of scope: per-model weekly limits (Opus/Sonnet), token counts, cost, history/charts, other OSes.

## Data

- Read the access token from `claudeAiOauth.accessToken` in `%USERPROFILE%\.claude\.credentials.json`. Also read `expiresAt`.
- Call `GET https://api.anthropic.com/api/oauth/usage` with `Authorization: Bearer <token>` and `anthropic-beta: oauth-2025-04-20`. Confirmed by a live call on 2026-10-27 (HTTP 200): read `five_hour.utilization` / `five_hour.resets_at` and `seven_day.utilization` / `seven_day.resets_at`. `utilization` is a percentage (0–100, e.g. `66.0`) and `resets_at` is an ISO 8601 UTC timestamp. Ignore all other fields. The response also has a `limits[]` array (`kind: "session"` / `"weekly_all"`, with `percent` and `severity`), which can serve as a fallback if the top-level fields disappear.
- **Never refresh the token and never write to the credentials file** ([ADR 0001](../../docs/adr/0001-read-usage-from-oauth-endpoint-read-only.md)). Re-read the file before each Update so the widget picks up tokens Claude Code has renewed.
- Update every 5 minutes and when the widget is clicked.

## Look

- Three looks, picked from the right-click menu and remembered. All three were tried in the prototype on 2026-10-27, and the user prefers B:
  - **B · Pill (default):** one line, `5h 7% · 7d 67%` with thin bars under each % and `↻ 3h 18m` at the end.
  - **A · Rings:** two ring gauges side by side, labelled `5h` and `7d`, with the % Usage in the centre.
  - **C · Strip:** a narrow vertical strip with two filling columns.
- Solid dark card with a thin light border, so it stands out on dark desktops.
- Colours: normal below 75%, amber from 75%, red from 90%.
- Every look shows a live countdown to the Session limit Reset (`3h 18m`).
- Hover tooltip: the Weekly limit Reset day and time (`Mon 09:00`) and the time of the last Update.

## Window

- Borderless and always on top of all normal windows. Not shown over exclusive full-screen apps.
- Drag anywhere on the widget to move it. Position is remembered across restarts.
- First launch: top-right of the primary screen. If the saved position is off every screen, move it back there.
- 85% opaque normally, 100% while the mouse is over it.
- No taskbar button and not in Alt+Tab.

## Docked mode

Validated in the prototype on 2026-10-27. The user wants both **Floating** (the window above) and **Docked**, and switches between them from the right-click menu ("Dock to taskbar" / "Undock"). The mode is remembered across restarts.

- Docked, the widget becomes **its own taskbar button**, chosen by the user on 2026-10-27. Windows 11 lets no app reserve space beside the tray, but it always makes room for an app button. Started last, the button lands next to **^**, and the user can drag it.
- Label: `70 28`, Weekly limit % then Session limit %. On a full taskbar Windows leaves about six characters, so there are no names or % signs.
- Icon: the time to the Session Reset as big digits (`1h`, then `45m` in the last hour), coloured with the Session thresholds, grey when Stale.
- Click the button, or Close window on its right-click menu: Undock.
- Rejected on 2026-10-27:
  - A pill floating over the taskbar: it covered icons, was too big and fell behind the taskbar when clicked.
  - A readout embedded in the taskbar as a child window: it draws nothing on Windows 11.
  - A readout owned by the taskbar beside the tray: it works, but covers a button when the taskbar is full.
  - Two pinned tray icons: they work, but sit right of **^**, and the user wants the readout left of it.

## Tray icon

This is the Floating-mode tray icon. Docked, the taskbar button takes its place.

- Draws the Session limit % as digits (`42`), coloured with the same thresholds.
- Hover tooltip: both Limits and the Session countdown.
- Left-click: hide or show the widget.
- Right-click menu: Show/Hide, Update now, Start with Windows (checkbox), Quit.
- Global hotkey **Ctrl+Alt+U** toggles hide/show.

## Stale and errors

- If an Update fails, rings and tray digits turn grey and keep showing the last known Usage (**Stale**).
- Hover hint by cause:
  - Token expired or rejected (401): "Open Claude Code to sign in again".
  - Network or server error: "Offline, last updated HH:mm".
  - No credentials file: "Sign in to Claude Code first".
- Start a normal Update again on the next tick. No retry storms.

## Startup and packaging

- .NET 10 WPF, published as a framework-dependent folder with `UseAppHost=false` and no installer. Company app control blocks unsigned `.exe` files in the user folder (seen 2026-10-27), so the app runs as `dotnet ClaudeUsageWidget.dll` through the signed `dotnet.exe`.
- Start with Windows is on by default, using a shortcut in the user's Startup folder that runs `dotnet.exe` with the DLL path. The tray checkbox adds or removes it.
- Settings (position, look, Floating or Docked, startup) are stored in `%APPDATA%\ClaudeUsageWidget\settings.json`.
- Only one copy runs at a time: launching it again shows the existing widget.

## Acceptance

- With Claude Code signed in, the widget shows the same % as `/usage` within one Update.
- The widget stays on top while switching between a browser, VS Code and Explorer.
- Its position survives a restart, and an off-screen position comes back on screen.
- Ctrl+Alt+U and tray left-click both hide and show it.
- Docked, it sits beside the tray, stays visible after clicking the taskbar and comes back Docked after a restart.
- Deleting or expiring the token makes it go grey with the right hint, without crashing.
- The credentials file's modified time does not change because of the widget.
