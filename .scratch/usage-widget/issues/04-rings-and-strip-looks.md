# 04 Rings and Strip looks, picked from the menu

Status: ready-for-agent
Type: task
Blocked by: 03

## What to build

Add the other two Floating looks and a right-click menu to pick between them. The choice is remembered in the settings file. See [spec.md](../spec.md) section Look.

- **A · Rings:** two ring gauges labelled `5h` and `7d`, % in the centre, countdown underneath.
- **C · Strip:** a narrow vertical strip with two columns filling from the bottom, % above each, countdown underneath.
- B · Pill stays the default.
- Right-click menu: Pill / Rings / Strip (the current one checked), Update now, Quit.
- All looks share the colour thresholds and the grey Stale state from 02.

## Acceptance criteria

- Each look shows the same numbers as the pill.
- The picked look survives a restart.
- Switching looks keeps the right edge in place.

## Comments
