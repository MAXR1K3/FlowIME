# FlowIME UI/UX R3.2 — Home page

R3.2 rebuilds the home page on top of the R3.0/R3.1 responsive and design-system foundation.

## Goals

- Make the current application and actual input state the primary visual anchor.
- Reduce the previous oversized “rule analysis” surface into a compact decision summary.
- Establish one clear primary action for the current application.
- Surface successful and failed rule edits inline instead of relying on silent state changes.
- Preserve P8C.2 behavior and all gameplay/input backends unchanged.

## Changes

- Header automation control is now a compact state control with an explicit running/paused label.
- Hero card keeps current application, context, process, and current input together.
- Primary action label adapts between “为此应用设置” and “编辑此应用规则”.
- “查看全部规则” is a tertiary navigation command.
- “规则解析” was replaced by a compact “当前决策” card with three rows: source, target, scene.
- Home rule edits now report success/failure through an inline InfoBar.
- Compact layout moves the input summary beneath the complete hero content instead of leaving an empty icon column.

## Functional boundary

No changes were made to Core, Windows input backends, Infrastructure, Probe, or App Services.
