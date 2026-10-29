# 07 Start with Windows, single copy, publish

Status: ready-for-agent
Type: task
Blocked by: 06

## What to build

Make it something the user runs every day. See [spec.md](../spec.md) section "Startup and packaging".

- Publish as a framework-dependent folder with `UseAppHost=false`, for example `dotnet publish -c Release -o dist`.
- Start with Windows, on by default: a shortcut in the user's Startup folder that runs `dotnet.exe "<path>\ClaudeUsageWidget.dll"`. The tray checkbox adds or removes it, and the choice is saved in settings.
- Single copy: starting it a second time shows the running widget and exits.
- A short `README.md` at the repo root: how to publish, start and quit, and the company app control note.

## Acceptance criteria

- After publishing and signing out and in, the widget starts on its own in its saved mode and position.
- Unticking Start with Windows removes the shortcut, and ticking it adds it back.
- Starting it twice leaves one widget running.

## Comments
