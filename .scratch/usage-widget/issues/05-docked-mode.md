# 05 Docked mode

Status: ready-for-agent
Type: task
Blocked by: 03

## What to build

The widget can be **Docked**: a compact readout that is part of the taskbar, beside the tray. The right-click menu switches between Floating ("Dock to taskbar") and Docked ("Undock"), and the mode is remembered. See [spec.md](../spec.md) section "Docked mode".

- Docked, hide the Floating window and show **two tray icons**: Session limit % and Weekly limit %, each as digits. Use WinForms `NotifyIcon`.
- Digits fill the small-icon size (bold Segoe UI, scaled to fit), coloured by threshold, grey when Stale, `!!` at 100%. Redraw only when the digits or colour change, and destroy the old icon handle.
- Tooltip: that Limit's % and its Reset.
- Left-click: Update. Right-click: Undock, Update now, Quit.
- Remove the icons on Undock and on Quit, so no ghost icons are left behind.

Reference: `prototype/UsageWidget.Prototype/TrayDock.cs` (2026-10-27).

## Acceptance criteria

- Dock: two icons appear in the tray, and the digits match the Usage.
- Once pinned, they stay visible next to the clock however full the taskbar is.
- Quit while Docked and start again: it comes back Docked.
- Undock: the Floating widget comes back where it was.
- Quit leaves no ghost icons.

## Comments

- 2026-10-27, from the prototype: a real child window of `Shell_TrayWnd` draws **nothing** on Windows 11. The taskbar has no redirection bitmap, and a layered child window (the usual fix) needs a Windows 8+ manifest in the exe; `dotnet.exe` doesn't have one (SetLayeredWindowAttributes fails with error 87). What works: a see-through top-level window **owned by** the taskbar (`GWLP_HWNDPARENT`), topmost, placed in screen pixels against the left edge of `TrayNotifyWnd` and moved again every second. It covers whatever taskbar button sits there, because Windows 11 can't make room.
- 2026-10-27: the user chose pinned tray icons (option 2) over the readout beside the tray, because their taskbar is always full and the readout covered a button.
- 2026-10-27: Windows 11 hides new tray icons behind **^**. The app pins its own two entries: under `HKCU\Control Panel\NotifyIconSettings`, find the subkeys whose `ExecutablePath` ends in `dotnet.exe` and whose `InitialTooltip` is ours, then set `IsPromoted` to 0 and then 1 while the icons are showing. Explorer only reacts to a change; a value that is already 1 stays hidden. Confirmed with a screenshot. Also: draw the digits big and scale them down to fit. A layout box the size of the icon clips the text to nothing.
- 2026-10-27: tray icons rejected: they sit right of **^**; the user wants the readout left of it. The user chose option C: the widget becomes **its own taskbar button**. Windows makes room for it like any app, and launched last it lands next to **^**. See `prototype/UsageWidget.Prototype/TaskbarDock.cs`: a minimized, off-screen window with its own AppUserModelID. Its label is `70 28` (Weekly %, then Session %) and its icon shows the time to the Session Reset (`1h`, coloured like the Session %), drawn at 24 px × scale with WM_SETICON. On a full taskbar Windows only leaves about six characters for the label, so it has no names or % signs. Clicking the button, or Close window, brings back the Floating widget. Confirmed with a screenshot. **This replaces the tray-icon design above; the scope and acceptance criteria need rewriting to match.**
