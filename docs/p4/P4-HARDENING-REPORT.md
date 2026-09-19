# FlowIME P4 Reliability Hardening Report

## Scope

P4 hardens the already accepted P1 input backend, P2 resident lifecycle and P3 rule-management flow. It deliberately does **not** add a new IME provider, broaden Microsoft-Pinyin conversion semantics, redesign the UI, or change the rules schema.

The phase has four bounded goals:

1. configuration durability and self-recovery;
2. bounded resident diagnostics;
3. wake/unlock recovery without restarting the P1 hooks;
4. a copyable health snapshot for later field failures.

## 1. Rules durability

### Authoritative file

`%LocalAppData%\FlowIME\rules.json`

### Last-known-good copy

`%LocalAppData%\FlowIME\rules.last-good.json`

Every successful write now follows this contract:

1. serialize to a unique temp file in the same directory;
2. flush the temp file through to disk;
3. parse the exact temp bytes back with the production schema;
4. replace `rules.json` only after validation succeeds;
5. independently write, flush and validate `rules.last-good.json`;
6. update the in-memory snapshot only after the authoritative file is committed.

A backup refresh failure does not turn a successful authoritative save into a false UI failure. It is recorded in repository diagnostics instead.

### Upgrade behavior

A valid pre-P4 `rules.json` is automatically copied into `rules.last-good.json` on first P4 load. No user edit is required to obtain the recovery copy.

### Corruption behavior

If `rules.json` cannot be parsed:

1. move it to `rules.corrupt-<UTC>.json`;
2. validate `rules.last-good.json`;
3. if valid, rebuild `rules.json` from that copy and continue with the recovered rules;
4. if the backup is also corrupt, preserve it as `rules.last-good.corrupt-<UTC>.json` and fail closed to an empty rule set.

Corrupt artifacts are capped at the five newest files. FlowIME-owned stale `rules*.tmp` files left by an interrupted write are cleaned on the next repository load/write.

The rules schema remains version 1.

## 2. Bounded resident logging

P1's native automation trace remains at:

`%LocalAppData%\FlowIME\logs\automation.log`

P4 replaces the old startup-only truncation policy with runtime rolling logs:

- current file target: 2 MiB;
- retained archives: 3;
- archive names: `automation.log.1`, `.2`, `.3`;
- oldest archive is deleted during rotation;
- trace I/O failure is swallowed by the listener and disables that sink rather than propagating into automation logic.

This means a long-running tray session cannot grow one automation log indefinitely, and a full/unavailable log destination cannot break P1 switching.

## 3. Resume / unlock recovery

The existing P2 tray host already owns a hidden Win32 top-level HWND for the full resident lifetime. P4 reuses it instead of creating another native window.

It observes:

- `WM_POWERBROADCAST / PBT_APMRESUMESUSPEND`;
- `WM_POWERBROADCAST / PBT_APMRESUMEAUTOMATIC`;
- `WM_WTSSESSION_CHANGE / WTS_SESSION_LOGON`;
- `WM_WTSSESSION_CHANGE / WTS_SESSION_UNLOCK`.

The tray HWND registers WTS notifications for the current session. Failure to register WTS notifications is non-fatal and is written to diagnostics.

A recovery notification does **not** tear down or recreate the P1 foreground/focus hooks. Instead:

1. multiple native notifications collapse by cancellation;
2. wait 750 ms for the desktop, target application and IME context to settle;
3. re-sample the actual current foreground state;
4. if automation is enabled, request the normal P1 current-foreground reapply path.

If automation is paused, status is refreshed but rule mutation remains paused.

## 4. Diagnostics snapshot

Settings now exposes **复制诊断信息**.

The report includes:

- UTC timestamp and FlowIME assembly version;
- Windows/.NET and process architecture;
- automation started/enabled/generation/in-flight state;
- last unexpected automation exception type/message, if any;
- startup-registration state;
- rule counts;
- rule repository health/recovery state;
- current process name, input profile/mode and matched rule ID;
- whether primary/backup rule files exist;
- current sizes of the four bounded automation log files.

It intentionally does not include typed text, clipboard contents, window title, password data, or full current executable paths. Corruption evidence is represented by file name only.

## Automated coverage added

P4 adds tests for:

- successful saves create a last-good copy;
- a pre-P4 primary file gets a backup on first P4 load;
- corrupt primary restores from the backup;
- missing primary restores from the backup;
- primary-only corruption preserves evidence and fails closed if no backup exists;
- primary + backup corruption preserves both artifacts and returns an empty rules set;
- corruption artifacts have bounded retention;
- stale temp files are removed;
- rolling log archives stay bounded;
- zero archive retention replaces the current file cleanly;
- resume/unlock native-message classification;
- explicit foreground-state resampling after a recovery request;
- automation runtime health snapshot reports the execution gate;
- Settings exposes the copy-diagnostics route;
- tray source contains Explorer recovery plus WTS/power recovery integration.

## Windows build gate

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\p0-check.ps1
```

P4 is not accepted until this remains green on the Windows development machine.

## P4 real-machine acceptance matrix

After the build gate passes:

1. Start P4 with the existing P3 rules. Confirm `%LocalAppData%\FlowIME\rules.last-good.json` appears without editing a rule.
2. Confirm Codex/Notepad switching still behaves exactly as the accepted P3 build.
3. Lock Windows (`Win+L`) while a configured app is active, unlock, wait roughly one second, and confirm the current app's rule is correct without an extra Alt+Tab.
4. Put the PC to sleep with FlowIME resident, wake it, return to the same configured app and confirm the rule is re-evaluated.
5. Pause automation, lock/unlock or sleep/wake, and confirm P4 does **not** force a rule while paused. Resume automation and confirm the current foreground rule applies normally.
6. Restart Explorer from Task Manager. Confirm the tray icon returns and switching still works.
7. In Settings choose **复制诊断信息**. Paste into Notepad and verify the report contains runtime/rules/log health but no typed content or clipboard payload.
8. Optional recovery test with FlowIME exited: make a safe copy of `%LocalAppData%\FlowIME`, deliberately corrupt only `rules.json`, start FlowIME, and confirm existing rules are restored from `rules.last-good.json` while a timestamped corrupt artifact is preserved. Restore the safe copy if anything is unexpected.
9. Leave FlowIME resident long enough to exercise logs, or temporarily reproduce rapid switching. Verify logging never interferes with input switching. Runtime rotation is unit-tested; do not artificially enlarge production logs unless needed.
10. Test one normal application launched as Administrator while FlowIME remains normal privilege. A failure must be bounded/no-hang; do not solve it by permanently elevating FlowIME.

For the final P4 pass, sample at least one application from each behavior family that is installed on the machine: traditional Win32, Chromium, Electron, Windows Terminal, packaged/WinUI, and WeChat. This is a regression matrix, not a promise that every third-party IME is supported; P4 still uses only the validated Microsoft-Pinyin backend.

## P4 completion boundary

If the build gate and the acceptance matrix pass, P4 is frozen. The next phase should be the IME-provider abstraction / third-party IME investigation. Further visual redesign, import/export, installer/update work and extra Microsoft-Pinyin tuning remain out of P4.
