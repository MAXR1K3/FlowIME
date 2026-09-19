# FlowIME UI/UX R3.2.1 — Home Polish

R3.2.1 implements the approved Home visual target while keeping the P8C.2 capability line frozen.

## Scope

- Adopt the user-confirmed FlowIME logo as a real UI asset.
- Add the logo to the WinUI TitleBar.
- Replace the Home automation card with a lightweight header control.
- Flatten the Hero current-input region so it is separated by hierarchy/divider rather than a nested card.
- Load the foreground application's real executable icon when Windows can provide it, with a generic fallback.
- Improve unknown input-state semantics: a known provider remains the primary label and the unreadable mode is described as `模式暂不可读`.
- Compress the current decision surface to three primary rows: source, target, context.
- Move verbose rule/context explanation behind an explicit `为什么这样切换？` disclosure action.
- Retain the R3 responsive structure/spacing split.

## Brand asset

`Assets/FlowIME.Logo.Source.png` preserves the exact user-confirmed source capture.
`Assets/FlowIME.Logo.png` is a transparent 44×44 runtime crop derived from that source for the WinUI title bar.

## Non-goals

No change to:

- input providers
- rule resolution
- gameplay/game-text-entry detection
- automation state machine
- overlay execution

## Validation performed in the build environment

- All XAML files parse as XML.
- XAML event-handler references resolve to code-behind methods.
- VisualState setter targets resolve to named XAML elements.
- P8C.2 Core/Windows/Infrastructure/Probe/Services are unchanged.

The build environment does not include the .NET SDK; final WinUI compilation and test execution must be run on Windows.
