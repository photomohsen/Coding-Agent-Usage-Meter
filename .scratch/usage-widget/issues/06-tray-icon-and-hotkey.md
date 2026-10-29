# 06 Tray icon and hotkey

Status: ready-for-agent
Type: task
Blocked by: 04, 05

## What to build

A tray icon and a global hotkey to hide and show the widget. See [spec.md](../spec.md) section "Tray icon".

- One tray icon drawing the Session limit % as digits (`42`), coloured by threshold and grey when Stale.
- Hover: both Limits and the Session countdown.
- Left-click: hide or show the widget, in whichever mode it is in (Floating or Docked).
- Right-click: Show/Hide, Update now, Start with Windows (checkbox, wired up in 07), Quit.
- Global hotkey **Ctrl+Alt+U** does the same as left-click. If the hotkey is already taken, say so in the tray tooltip instead of failing.

## Acceptance criteria

- The tray icon digits match the widget.
- Left-click and Ctrl+Alt+U both hide and show the widget, Floating and Docked.
- Quit removes the tray icon straight away, with no ghost icon left behind.

## Comments

- 2026-10-27: Docked mode is now two tray icons (see 05). This single icon is for Floating mode only; Docked, the two Docked icons replace it. Ctrl+Alt+U still hides and shows the widget in both modes, and in Docked mode that means hiding and showing the two icons.
