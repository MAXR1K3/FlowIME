# FlowIME P8B.1 Fullscreen Observation Report

## Scope

P8B.1 is the first Gameplay Context phase. It adds **reliable fullscreen sensing**
without changing the user's input method policy.

This is intentionally observation-only:

- it emits the existing `Fullscreen` context signal;
- it does **not** emit `Game` yet;
- it registers no gameplay context policy;
- it never forces English merely because a window is fullscreen.

The reason for this split is correctness. Fullscreen geometry is observable with
strong Win32/DWM signals, while "this fullscreen window is a game" is not a universal
Windows property. Browsers, video players, presentation software, remote desktop and
games may all occupy one monitor. A gameplay policy must therefore be layered on top
of fullscreen sensing rather than equating the two.

## Runtime integration

P8A established:

```text
Foreground/focus
  -> WindowResolver
  -> InputContextEngine
  -> InputDecisionEngine
  -> ManualOverrideGuard
  -> provider backend
```

P8B.1 registers one production detector:

```text
InputContextEngine
  -> core Application signal
  -> windows.fullscreen.geometry
       -> Fullscreen signal when geometry qualifies
```

`InputDecisionEngine` still has zero production context policies, so application
rules and the global default resolve exactly as in accepted P8A.1.

## Detection model

The native probe collects only presentation state:

- `IsWindowVisible`;
- `IsIconic`;
- DWM cloaked state;
- DWM extended frame bounds, with `GetWindowRect` fallback;
- bounds of the nearest monitor from `MonitorFromWindow` + `GetMonitorInfo`.

It does not collect title text, keyboard input, URLs, document contents or clipboard
content.

A window qualifies as fullscreen only when:

1. it is visible;
2. it is not minimized;
3. it is not DWM-cloaked;
4. both window and monitor geometry are valid;
5. all four window-frame edges match the physical monitor bounds within 4 pixels.

The detector compares against **monitor bounds**, not monitor work-area bounds. A
normal maximized window that stops above a visible taskbar therefore does not become
a fullscreen signal.

DWM extended frame bounds are preferred because they exclude invisible resize borders
that can make borderless fullscreen windows appear several pixels larger when using
raw `GetWindowRect`.

## DPI and multi-monitor behavior

FlowIME already runs Per-Monitor-V2 DPI aware. The fullscreen detector works in
native pixel coordinates throughout and therefore does not mix XAML effective pixels
with Win32 monitor geometry.

Negative monitor origins are explicitly supported, covering monitors positioned to
the left or above the primary display.

P8B.1 intentionally treats a window spanning multiple monitors as not being a normal
single-monitor fullscreen window unless its frame matches the selected monitor bounds.
Multi-monitor/span gaming can be added later as a separate presentation mode instead
of weakening the single-monitor detector.

## Confidence contract

`Fullscreen` is emitted with `ContextSignalConfidence.High`.

This confidence refers only to the presentation fact "the foreground window covers a
monitor as fullscreen". It does not imply that the application is a game.

## Observability

P8A already records context signal kinds in the bounded decision journal and exposes
them through diagnostics. P8B.1 reuses that path; no new user-facing status panel is
added.

In Settings -> Diagnostics, `currentContextSignals` should read approximately:

```text
currentContextSignals=Application
```

for an ordinary window, and:

```text
currentContextSignals=Application,Fullscreen
```

when the currently sampled foreground window is fullscreen.

This keeps implementation diagnostics out of the normal user UI while making the
new detector directly testable on real machines.

## Automated coverage

P8B.1 adds tests for:

- exact monitor-sized fullscreen geometry;
- small DWM/frame variance inside the tolerance;
- maximized work-area windows with a visible taskbar;
- hidden/minimized/cloaked windows;
- unavailable geometry;
- cancellation before native sampling;
- negative multi-monitor coordinates;
- oversized frames beyond tolerance;
- invalid negative tolerance.

The source tree now contains 218 `[Fact]`/`[Theory]` test definitions, up from 209 in
accepted P8A.1.

## Preserved invariants

P8B.1 does not modify:

- FlowIME.Core decision/coordinator code;
- rule priority;
- application/global-default persistence;
- ManualOverrideGuard semantics;
- Microsoft Pinyin or WeChat provider mutation code;
- tray/menu behavior;
- UI pages or UI information hierarchy.

No input-method mutation is introduced by fullscreen detection itself.

## Windows acceptance gate

From the repository root:

```powershell
dotnet test -c Debug
dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug
```

Then test detector observation:

1. Put a normal window in the foreground and copy Settings -> Diagnostics.
   Confirm `currentContextSignals=Application`.
2. Put a browser/video player into true fullscreen and copy diagnostics.
   Confirm `Fullscreen` appears. This is expected and proves fullscreen is not being
   conflated with game classification.
3. Launch one fullscreen/borderless game and copy diagnostics while it is foreground.
   Confirm `Fullscreen` appears.
4. Return the game to a normal window or Alt+Tab to a normal desktop app. Confirm the
   `Fullscreen` signal disappears after the foreground/context refresh.
5. Re-run the accepted Codex/Notepad/global-default behavior check. Input switching
   must remain unchanged.

## Next P8B step

P8B.2 should add **Gameplay Eligibility**, not `fullscreen == game`.

The recommended decision inputs are:

- high-confidence `Fullscreen` presentation signal;
- explicit per-application gameplay enable/disable preference;
- conservative process/window characteristics as supporting evidence only;
- optional Windows/game-specific adapters later.

Only after gameplay eligibility is stable should FlowIME register a `GameplayPolicy`
that defaults gameplay to English. `GameTextEntry` will be designed with higher
specificity/priority so chat/text entry can temporarily override the gameplay target
without dismantling the base policy.
