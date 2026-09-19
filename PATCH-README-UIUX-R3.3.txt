FlowIME UI/UX R3.3 — Rules Management Redesign

Baseline: UI/UX R3.2.2 / P8C.2 feature line frozen.

What changed
- Global default is now a whole-row configuration target with status + chevron.
- Application rules are presented as a grouped settings-style list rather than separate management cards.
- Clicking a rule row opens the edit dialog.
- Persistent actions are reduced to enable/disable + overflow.
- Edit/Delete moved into the overflow menu.
- Full executable paths are no longer always visible; the executable filename is shown and the full path remains available as a tooltip/search target.
- Rule rows load and display real application icons when Windows can provide them.
- Search, empty states, diagnostics and inline success/error feedback remain intact.
- Compact and desktop rule rows retain explicit responsive states.

Frozen feature areas confirmed unchanged
- FlowIME.Core
- FlowIME.Windows
- FlowIME.Infrastructure
- FlowIME.Probe
- FlowIME.App/Services

Local validation available in the generation environment
- 11 XAML files parse as XML.
- XAML event handlers resolve to code-behind methods.
- VisualState Setter targets resolve to x:Name elements.
- Patch replay is compared against the full R3.3 tree before delivery.

Windows validation
  dotnet test -c Debug
  dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug
