# PROTOTYPE: widget look (throwaway)

**Verdict (2026-10-27):** keep all three Floating looks with B · Pill as the default, plus Docked mode embedded in the taskbar. Both are recorded in `.scratch/usage-widget/spec.md`. There's no git repo yet, so this folder stays as the record of the prototype until it can go on a throwaway branch.

Question: which look works best floating on the real desktop with real Usage?

Three variants of the widget, switchable with ← / → or the right-click menu:

- **A · Rings**: two ring gauges, Session countdown underneath (the spec's choice)
- **B · Pill**: one line of text with thin bars
- **C · Strip**: a narrow vertical strip with two filling columns

Run from the repo root:

```
dotnet run --project prototype/UsageWidget.Prototype
```

Second question: does docked mode feel like part of the taskbar? Right-click → **Dock to taskbar**. It becomes a two-line readout the size of the clock, embedded in the taskbar as a child window just left of the tray. Right-click it to undock.

History (2026-10-27): a pill floating over the taskbar was rejected because it covered icons and was too big. Tray icons were tried next by mistake (the user wanted docked mode fixed, not replaced) and removed. Win11 cannot make the taskbar move its icons to give us room.

Click: update · drag: move · hover: full state · right-click: variants, update, quit.

Not in the prototype: tray icon, hotkey, startup, saved position, single-file exe.

## Docked mode: what finally drew

- Child of the taskbar (SetParent): invisible on Win11, even with CPU rendering. A layered child is refused under dotnet.exe (error 87).
- **Works:** a see-through window owned by the taskbar, kept topmost at the tray's left edge. It covers a button if the taskbar is full.
- **Final choice (user, 2026-10-27):** two pinned tray icons, Session % and Weekly %, because the taskbar is always full. See `TrayDock.cs`. The readout file `DockWindow.cs` was removed.
