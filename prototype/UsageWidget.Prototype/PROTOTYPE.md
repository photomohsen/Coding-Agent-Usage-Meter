# PROTOTYPE: widget look (throwaway)

**Verdict (2026-10-27):** keep all three Floating looks with B · Pill as the default, plus a tray icon next to the clock (2026-10-31; Docked mode was dropped). Both are recorded in `.scratch/usage-widget/spec.md`. There's no git repo yet, so this folder stays as the record of the prototype until it can go on a throwaway branch.

Question: which look works best floating on the real desktop with real Usage?

Three variants of the widget, switchable with ← / → or the right-click menu:

- **A · Rings**: two ring gauges, Session countdown underneath (the spec's choice)
- **B · Pill**: one line of text with thin bars
- **C · Strip**: a narrow vertical strip with two filling columns

Run from the repo root:

```
dotnet run --project prototype/UsageWidget.Prototype
```

Second question: how should it sit by the taskbar? Since 2026-10-31: a **tray icon** with two bars (Claude, Codex), or two rings if picked in Settings, that shows or hides the Floating widget, and the widget snaps 8 px above the clock when dropped near it. It hides while a full-screen app is open. See `TrayIcon.cs`.

History: a pill floating over the taskbar covered icons; a readout embedded in the taskbar draws nothing on Win11; a readout owned by the taskbar covered a button; two digit tray icons and then a taskbar button (Docked mode, `TaskbarDock.cs`) read as random numbers with no names or colour. Win11 lets no app draw inside the taskbar, so the tray icon is the one place left.

Click: update · drag: move · hover: full state · right-click: variants, update, quit.

Not in the prototype: hotkey, startup, saved position, single-file exe.
