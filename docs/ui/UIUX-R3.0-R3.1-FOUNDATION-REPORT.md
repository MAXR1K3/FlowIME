# FlowIME UI/UX R3.0 + R3.1 Foundation Report

## Scope

This phase intentionally freezes the P8C.2 feature baseline. It changes only the presentation layer and UI contract tests.

### R3.0 — Layout correctness

- Replaced sibling VisualState dependency on Home, Rules, Settings, and About pages with orthogonal state groups:
  - structure states decide rows, columns, orientation, and action placement;
  - spacing states decide page padding and width-related presentation values.
- Fixed the wide-window regression where entering a `Wide` state reset layout setters from the previous `Medium/Comfortable` state.
- Added desktop content width caps:
  - primary pages: 1120 effective pixels;
  - settings/support pages: 960 effective pixels.
- Preserved compact NavigationView safe-space padding at widths below 760 effective pixels.

## R3.1 — Design system foundation

Added shared UI tokens and styles in `App.xaml`:

- `FlowPageContentMaxWidth`
- `FlowSettingsContentMaxWidth`
- `FlowControlMinHeight` (40 epx)
- `FlowIconButtonSize` (40 epx)
- `FlowSectionLabelStyle`
- `FlowSettingRowStyle`
- `FlowStatusPillStyle`
- `FlowPrimaryButtonStyle`
- `FlowSecondaryButtonStyle`
- `FlowSubtleButtonStyle`
- `FlowIconButtonStyle`

The first command hierarchy is applied to Home, Rules, and Settings without changing command behavior.

## Behavioral invariants

The following are intentionally unchanged from UIUX R2.2 / P8C.2:

- application rule resolution;
- global default resolution;
- Gameplay detection;
- Gameplay US keyboard baseline;
- Gameplay hotkey guard;
- input status overlay runtime behavior;
- GameTextEntry detection and profiles;
- input providers and automation coordinator.

## Validation performed in the build environment

- All 11 XAML files parse as XML.
- Every VisualState Setter target resolves to an `x:Name` in its XAML file.
- XAML event-handler references resolve to code-behind methods.
- No production Core/Windows/Infrastructure/Probe/App Services files were changed.
- UI contract tests were updated for the R3 responsive architecture and three new R3 contract tests were added.

A Windows .NET SDK is not available in the generation environment, so final WinUI compilation and `dotnet test` must be run on the target Windows machine.
