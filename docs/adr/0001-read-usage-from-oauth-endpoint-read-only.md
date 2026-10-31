# Read Usage from the OAuth usage endpoint, with the token read-only

The widget gets Session and Weekly Usage by calling Anthropic's undocumented `api.anthropic.com/api/oauth/usage` endpoint with the access token Claude Code stores in `~/.claude/.credentials.json`. It never refreshes that token itself: refreshing rotates the refresh token and could sign Claude Code out. When the token has expired, the widget runs `claude auth status` (hidden, at most every 10 minutes), which makes the CLI refresh and save the token the way any claude command does. Only the command-line claude keeps this file current: the desktop app has its own sign-in, so the file goes stale while the user works in the app. If the CLI isn't installed or can't refresh, the widget shows its last known Usage as Stale with "Token expired".

## Considered Options

- **Claude Code status line feed**: documented, but only updates while a Claude Code session is open and means taking over the user's status line setting. Rejected for interfering with Claude Code.
