# Read Usage from the OAuth usage endpoint, with the token read-only

The widget gets Session and Weekly Usage by calling Anthropic's undocumented `api.anthropic.com/api/oauth/usage` endpoint with the access token Claude Code stores in `~/.claude/.credentials.json`. It never refreshes that token itself: refreshing rotates the refresh token and could sign Claude Code out. When the token has expired, the widget shows its last known Usage as stale until Claude Code refreshes it.

## Considered Options

- **Claude Code status line feed**: documented, but only updates while a Claude Code session is open and means taking over the user's status line setting. Rejected for interfering with Claude Code.
