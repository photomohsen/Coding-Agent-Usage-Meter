# Coding Agent Usage Meter

**A small Windows widget that shows how much of your Claude Code and Codex limits you have used.**

![The widget: Claude on top, Codex below](docs/images/pill.png)

## What it does

Claude Code and Codex both stop you when you hit a limit: one that resets every **5 hours** and one that resets every **7 days**. This widget sits on your desktop and shows both, so you always know how much you have left.

- **5h** and **7d** show how much of each limit is used. The bar turns amber at 75% and red at 90%.
- **↻ 2h 13m** is the time until the 5-hour limit resets.
- It updates by itself every few minutes. Click it to update now.
- If it can't update (offline, signed out), the numbers turn grey and it says why.

Codex shows up by itself if you have it installed. If not, you only see Claude.

> For **Windows 11** only. Not made by or connected to Anthropic or OpenAI.

## Install

You need:

- **Windows 11**
- **Claude Code**, signed in with a Claude Pro or Max plan. Check by running `claude` in a terminal.
- **Codex** (optional), signed in with ChatGPT

### The quick way

Open **PowerShell** (Start → type `PowerShell`), paste this line and press Enter:

```powershell
irm https://raw.githubusercontent.com/photomohsen/Coding-Agent-Usage-Meter/main/install.ps1 | iex
```

It downloads the widget to `%LOCALAPPDATA%\Coding Agent Usage Meter`, installs .NET 10 if you don't have it, puts a **Coding Agent Usage Meter** shortcut on your desktop and starts the widget. Paste the same line again later to update it.

### Or download the ZIP

**1. Download it.** On this page click **Code → Download ZIP**, then unzip it to a folder you'll keep (for example `Documents\Coding Agent Usage Meter`). The widget runs from this folder, so don't delete it.

**2. Double-click `Install.cmd`.** It:

- installs .NET 10 if you don't have it
- builds the widget
- puts a **Coding Agent Usage Meter** shortcut on your desktop
- starts the widget

If Windows shows *"Windows protected your PC"*, click **More info → Run anyway**.

**3. That's it.** The widget appears above the clock. Drag it anywhere.

### Starting it later

- Double-click **Coding Agent Usage Meter** on your desktop.
- Or let it start by itself: right-click the widget → **Settings** → turn on **Start with Windows**.

To quit, right-click the widget → **Quit**.

<details>
<summary>Install by hand instead</summary>

```bash
winget install Microsoft.DotNet.SDK.10
git clone https://github.com/photomohsen/Coding-Agent-Usage-Meter.git
cd Coding-Agent-Usage-Meter
dotnet build prototype/UsageWidget.Prototype
```

Then double-click `Start Widget.vbs` to start it.

</details>

### Update to a new version

Download the new ZIP, unzip it over the old folder, and double-click `Install.cmd` again. Your settings are kept.

## Use it

| Do this | To |
|---|---|
| Click | Update now |
| Drag | Move it (it remembers where). Drop it near the clock and it snaps into place just above it |
| Hover | See a card with both limits, their reset times, and why the numbers are old if they are |
| Right-click | Open the menu: Settings, Compact, Quit |
| Ctrl + mouse wheel | Make it bigger or smaller |
| ← / → | Switch look |

### Looks

| Pill (default) | Rings | Strip |
|---|---|---|
| ![Pill](docs/images/pill.png) | ![Rings](docs/images/rings.png) | ![Strip](docs/images/strip.png) |

There are seven more to try, in **Settings → Look** or with ← / →:

| Look | Shows |
|---|---|
| Gauge | A half dial: 5h outside, 7d inside |
| Meter | Ten blocks per limit; the last three are amber and red |
| Target | A double ring around the logo, like the tray icon |
| Numbers | Just the two numbers, big, amber and red when high |
| Mini | One thin line per agent: the higher % and the time to reset |
| Pace | The Pill's bars plus a tick for an even pace; says "fast" when you're ahead of it |
| Halo | Only the rings around each logo, no text. Hover a ring for the numbers |

In **Settings** you can also make it **Compact** (less space) or change its size.

### Tray icon

Next to the clock there is a small icon with two bars: Claude on top, Codex below. Each bar fills with the usage and turns amber at 75% and red at 90%. It turns grey when the numbers are old.

In **Settings → Tray icon** you can pick **Rings** instead of bars: Claude is the outer ring and Codex the inner one. Without Codex, the outer ring is the 5-hour use and the inner ring the 7-day use.

![Tray icon](docs/images/tray.png)

| Do this | To |
|---|---|
| Hover the icon | See the numbers |
| Click the icon | Hide or show the widget |
| Right-click the icon | Update now, Settings, Quit |

The widget also hides by itself while a full-screen app, game or presentation is open.

![Settings](docs/images/settings.png)

It also lists any local dev servers you have running (`localhost:3000` and so on), under the look. Click `localhost:3000` to open it, or **Close** to stop it; Close asks first. Hover a row to see the project folder and command.

## Is it safe?

Yes. It only reads, on your own PC, the sign-in that Claude Code and Codex already saved, and asks Anthropic and OpenAI for your usage. Nothing is sent anywhere else, and it never changes your sign-in.

Each PC shows the usage of whoever is signed in on it.

<details>
<summary>Details for developers</summary>

| Agent | Reads | Asks |
|---|---|---|
| Claude Code | `~/.claude/.credentials.json` | `api.anthropic.com/api/oauth/usage` |
| Codex | `~/.codex/auth.json` | `chatgpt.com/backend-api/wham/usage` |

- The tokens are read-only. When the Claude token has expired, the widget runs `claude auth status` in the background (at most every 10 minutes) so the Claude CLI refreshes it.
- When Codex can't be reached, it shows the last limits from Codex's own logs (`~/.codex/sessions`), in grey.
- These endpoints aren't documented, so a change on their side can break the widget.
- Settings are saved in `%APPDATA%\ClaudeUsageWidget\settings.json`.
- It runs through `dotnet.exe` instead of its own `.exe`, so it also works where unsigned programs are blocked.
- `dotnet <dll> --qa <folder>` draws every look and state to PNG files with made-up numbers.
- Why it works this way: [docs/adr](docs/adr).

</details>

## License

[MIT](LICENSE)
