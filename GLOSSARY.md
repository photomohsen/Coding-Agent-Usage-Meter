# Claude Usage Widget

A small always-on-top desktop widget that shows how much of the user's Claude plan limits has been used.

## Language

**Usage**:
The share of a Limit already used in its current Window, as a percentage.
_Avoid_: Tokens, cost, consumption

**Limit**:
A cap on how much the Claude plan can be used within a Window. Shared by Claude Code and claude.ai.
_Avoid_: Quota, rate limit

**Session limit**:
The Limit over a rolling 5-hour Window.
_Avoid_: 5h limit, hourly limit

**Weekly limit**:
The Limit over a 7-day Window, across all models.
_Avoid_: 7d limit, Opus limit

**Window**:
The period a Limit is counted over; it ends at its Reset.
_Avoid_: Period, cycle

**Reset**:
The moment a Window ends and its Usage returns to zero.
_Avoid_: Refresh, renewal

**Floating**:
The widget as a small window above all others, which the user can drag anywhere.
_Avoid_: Undocked, overlay, popup

**Docked**:
The widget as a compact readout that is part of the taskbar, beside the tray.
_Avoid_: Attached, pinned, taskbar mode

**Update**:
One check with Anthropic for the latest Usage, made on a timer or on click.
_Avoid_: Refresh, sync, poll

**Stale**:
Usage shown that could not be confirmed by the latest Update.
_Avoid_: Old, offline, cached
