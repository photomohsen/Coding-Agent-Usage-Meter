# 03 Remember position (settings file)

Status: ready-for-agent
Type: task
Blocked by: 01

## What to build

Settings in `%APPDATA%\ClaudeUsageWidget\settings.json`, starting with the Floating position. Later tickets add the look (04), Floating or Docked (05) and startup (07) to the same file. See [spec.md](../spec.md) sections Window and "Startup and packaging".

- Save the position after each drag.
- On start, restore it. If it's off every screen (for example a monitor was unplugged), use the top right of the primary screen instead.
- A missing or corrupt settings file falls back to defaults without crashing.

## Acceptance criteria

- Drag the widget, quit and start it again: it comes back in the same place.
- Edit the saved position to `-5000, -5000` and start it: it appears top right.
- Delete the settings file: it starts with defaults.
- Unit tests cover the off-screen check and loading a corrupt file.

## Comments
