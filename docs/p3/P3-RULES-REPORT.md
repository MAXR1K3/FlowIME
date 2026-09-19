# FlowIME P3 Rules/Productization Report

## Scope

P3 builds on the P1 input-state backend and P2 lifecycle shell. It does not add a new IME backend or arbitrary third-party IME selection. The rule action model remains the proven V1 set: **Keep / Chinese / English**.

## Implemented

### Current-application rule workflow

- Home retains the one-click **create rule for current application** action.
- The current application is resolved from the last external foreground snapshot, so opening FlowIME itself does not erase the target.
- The picker shows application display name, process name, executable path, and loads the executable icon when Windows allows it.
- If the executable already has a rule, the current-app flow enters update mode and generic Add asks whether to update it instead of creating a duplicate.
- Upsert preserves the existing rule ID and priority.

### Current match status

- The Home rule card now reports either `current app → input profile · Chinese/English`, Keep, or an explicit `当前未匹配规则` state.
- The status snapshot carries the actual matched `ApplicationRule`, not only the action.
- Foreground status observation now follows both foreground and input-focus WinEvents, so same-application focus changes can refresh the displayed input state without writing it.

### Rule management

Rules page now supports:

- enable/disable;
- edit Keep / Chinese / English;
- delete with confirmation;
- duplicate-executable upsert instead of duplicate rows.

The JSON rules schema is unchanged.

### Temporary pause

- Home's automation switch now pauses rule execution without unregistering foreground/focus hooks.
- Pausing cancels the current generation and waits for that cancellation to settle.
- Events continue to be observed while paused, but no new rule mutation is scheduled.
- Resuming immediately re-applies the rule for the currently foreground application.
- The tray menu exposes dynamic `暂停自动切换` / `恢复自动切换` commands.
- Pause is intentionally temporary and is not persisted across process restarts.

## Deliberately deferred

- arbitrary input-method/profile selection beyond the proven Microsoft Pinyin V1 backend;
- rule import/export;
- advanced priority editing or window-title-specific authoring UI;
- diagnostics-copy UI;
- installer/update channel.

## Windows acceptance checklist

1. Add a rule from Home while the target application is the last external foreground app. Verify name/process/path/icon are correct.
2. Add the same executable again. Verify FlowIME asks to update and the Rules page still contains one row.
3. Edit the rule English → Chinese. Verify Home reflects the edited rule immediately; then return to the target application and verify P1 applies Chinese on foreground entry.
4. Disable the current rule. Verify Home reports it as unmatched and subsequent target focus/foreground events do not force a mode.
5. Re-enable it. Return to the target and verify rule application resumes.
6. Delete it. Verify Home reports `当前未匹配规则`.
7. Pause from Home. Switch among configured apps and confirm no rule changes occur.
8. Resume from Home, then return to a configured app and verify its rule is applied. FlowIME must not mutate the last external app while the FlowIME window itself is foreground.
9. Pause/resume from the tray menu while an external target remains foreground; verify the current target is re-evaluated and the Home toggle later reflects the same state.
10. Re-run `scripts\p0-check.ps1` and confirm build/tests remain green.
