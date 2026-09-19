# FlowIME P1 Stability State Machine Report

## Status

**SOURCE IMPLEMENTED — Windows build/unit tests still required.**

The implementation was prepared from the current FlowIME source snapshot. The
execution sandbox used for this patch does not contain the .NET SDK and cannot
reach the public network to install SDK `10.0.401`, so `dotnet restore`,
`dotnet build`, and `dotnet test` could not be executed here. Do not treat this
report as a Windows runtime pass.

## P1 behavior

- `EVENT_SYSTEM_FOREGROUND` and `EVENT_OBJECT_FOCUS` feed the same
  last-event-wins coordinator.
- Every valid event creates a new generation and cancels the previous operation.
- Every operation waits for the configured debounce and then re-reads the actual
  foreground HWND.
- Every bounded retry re-resolves the foreground `WindowContext` instead of
  reusing data captured by the original event.
- Focus events are filtered to the current foreground process before they reach
  the coordinator.
- The focus WinEvent hook uses `WINEVENT_SKIPOWNPROCESS`; the foreground hook
  intentionally does not, because a transition into FlowIME itself must cancel
  any rule operation still running for the previously foreground application.
- Default retry schedule is 3 attempts: initial, +35 ms, +100 ms.
- There is no fixed-frequency polling loop.

## Microsoft Pinyin mutation contract

Rules that request Chinese or English still target Microsoft Pinyin.

- Chinese conversion mode: `0x401`
- English conversion mode: `0x0`
- A reliable `GetGUIThreadInfo` focus/caret input HWND is required; top-level
  fallback is not mutated.
- The backend reads before writing and performs no write when the exact requested
  conversion value is already present.
- Focus is resolved again immediately before mutation and again before read-back
  verification.
- Microsoft Pinyin TSF activation is verified before any Microsoft-Pinyin-specific
  conversion flags are written.
- If the pre-write profile guard had to switch away from another IME (for example
  WeChat) back to Microsoft Pinyin, the conversion write is deferred and retried
  after the profile/input context settles.
- A newly activated Microsoft Pinyin profile waits 25 ms before each settle sample;
  focus is re-resolved on every sample and the requested conversion mode must remain
  stable for four samples before success is reported.

## Diagnostics

The app installs a bounded file Trace listener. Runtime diagnostics are written to:

`%LocalAppData%\FlowIME\logs\automation.log`

The file is reset on startup after it grows beyond 2 MiB. Coordinator records
include generation, trigger, stage, foreground HWND, attempt, process, rule,
action, before/after mode, and failure code. Microsoft Pinyin backend records
include top-level HWND, resolved focus HWND, requested conversion mode, and
observed conversion mode.

## Added/updated regression coverage

The source test suite now covers, in addition to the existing P0 tests:

- same-application focus changes re-apply a rule even long after foreground entry;
- a focus event supersedes a pending foreground generation;
- rapid A -> B -> A transitions apply only the final A generation;
- retry re-resolves `WindowContext`;
- retry stops if the real foreground HWND changed before the next WinEvent arrives;
- focus is re-resolved immediately before conversion-mode write and verification;
- an unverified Microsoft Pinyin profile guard produces no conversion-mode write;
- profile-switch settling re-resolves focus and requires stable read-back.

## Windows build gate

From `C:\FlowIME` run:

```powershell
.\scripts\p0-check.ps1
```

That command performs restore, Debug build, and the full unit-test solution run.

## P1 manual regression matrix

After the build gate is green, use the normal FlowIME app and test at least:

1. Codex rule = Chinese; Notepad rule = English; switch between them repeatedly.
2. In Codex manually change Microsoft Pinyin to English, switch to Notepad, then
   return to Codex; Codex must be corrected to Chinese.
3. Start from WeChat IME, enter an app whose rule requests Chinese/English; FlowIME
   must first switch to Microsoft Pinyin and then reach the requested mode.
4. Rapidly Alt+Tab A -> B -> A; the final A rule must win and no delayed B write may
   appear afterward.
5. Change between multiple text fields inside the same matched application; each
   real focus entry must be revalidated.
6. Close and restart a matched application; the new HWND must still receive its rule.
7. Open FlowIME itself while another rule operation is settling; no stale write may
   occur after FlowIME becomes foreground.

If any case fails, preserve `%LocalAppData%\FlowIME\logs\automation.log` for the
next diagnosis instead of adding more blind retry loops.
