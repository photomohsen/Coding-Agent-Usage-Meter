# Coding Agent Usage Meter

A small always-on-top desktop widget that shows how much of the user's coding agent plan limits (Claude Code, Codex) has been used.

## Language

**Usage**:
The share of a Limit already used in its current Window, as a percentage.
_Avoid_: Tokens, cost, consumption

**Limit**:
A cap on how much the Claude plan can be used within a Window. Shared by Claude Code and claude.ai. Codex has its own Limits, set by the ChatGPT plan.
_Avoid_: Quota, rate limit

**Codex Usage**:
Usage of the Codex Limits (its own Session and Weekly limit), shown under Claude's when Codex is on the machine.
_Avoid_: OpenAI usage, ChatGPT usage

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
_Avoid_: overlay, popup

**Tray icon**:
The small icon next to the clock with two bars, Claude on top and Codex below, or the same two as rings (Claude outside). Click it to hide or show the Floating widget. It replaced Docked mode (a taskbar button) on 2026-10-31.
_Avoid_: Docked, taskbar button, notification icon

**Update**:
One check with Anthropic for the latest Usage, made on a timer or on click.
_Avoid_: Refresh, sync, poll

**Stale**:
Usage shown that could not be confirmed by the latest Update.
_Avoid_: Old, offline, cached
