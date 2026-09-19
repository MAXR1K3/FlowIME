# FlowIME P8A Context Awareness Foundation Report

## Scope

P8A is an architecture phase. It does **not** enable fullscreen-game forcing,
browser-address-bar switching, game chat detection, domain rules, or automatic
manual-override inference.

The accepted P7.2.2 behavior remains the reference behavior. With no context
detectors/policies registered, the same application rule and global-default target
must reach the same provider backend as before.

P8A inserts stable boundaries so later context-aware features do not accumulate
provider-specific or feature-specific `if` branches inside `AutomationCoordinator`.

## Runtime pipeline

P7 and earlier:

```text
Foreground/focus event
  -> WindowResolver
  -> RuleEngine
  -> InputMethodBackend
```

P8A:

```text
Foreground/focus event
  -> WindowResolver
  -> ApplicationIdentity
  -> InputContextEngine
       -> zero or more isolated detectors
       -> Context signals
  -> InputDecisionEngine
       1. Context policy
       2. Application rule
       3. Global default
       4. No target
  -> ManualOverrideGuard
  -> InputMethodBackend
  -> AutomationDecisionJournal
```

The provider layer remains unchanged. Microsoft Pinyin and WeChat Input Method still
own their validated mutation semantics from P5.

## 1. Stable application identity

`ApplicationIdentity` is runtime identity, not a replacement for the user-visible
application name.

Precedence:

1. Application User Model ID (AUMID), when Windows exposes it;
2. Package Family Name (PFN);
3. normalized executable path for unpackaged applications, represented in runtime
   diagnostics as a SHA-256-derived opaque key rather than the full path;
4. executable file name for versioned `WindowsApps` paths when package identity is
   unavailable;
5. process name as the final fallback.

This deliberately avoids treating a version-bearing WindowsApps path as the stable
identity of Codex/Store/MSIX applications.

P8A adds native `GetApplicationUserModelId` discovery alongside the existing package
family lookup. `WindowContext` and `RunningApplication` carry both optional values.

### Stable identity in persisted application rules

P8A adds an optional `ApplicationMatch.ApplicationUserModelId` field while keeping
`schemaVersion: 2`. This is a backward-compatible additive field: existing rules
that do not contain it still deserialize exactly as before, so no migration is
required. `ApplicationMatch.PackageFamilyName` already existed.

When FlowIME creates a rule from a running application, it now persists the identity
material Windows exposes:

- the current executable path, for display and executable-name fallback;
- the Package Family Name, when available;
- the Application User Model ID, when available.

Rule matching uses strongest-observed-first identity semantics. If both the rule and
the current process expose AUMID, AUMID is authoritative and a mismatch rejects the
rule. If runtime AUMID lookup is temporarily unavailable, matching degrades to PFN +
executable name, then to the legacy path/name fallback. This avoids making a valid
rule brittle because one optional Windows identity lookup failed.

Rule authoring/upsert likewise treats the same AUMID as the same app; when AUMID is
unavailable, it falls back to the same PFN + executable file name. Therefore a
versioned WindowsApps path update replaces the existing rule instead of creating a
duplicate.

## 2. Context signal model

`InputContextEngine` owns detection only. It never chooses an input method and never
mutates one.

Every context contains an `Application` signal. Optional detectors may later add:

- `Fullscreen`
- `Game`
- `TextInput`
- `BrowserAddressBar`
- `GameTextEntry`

Signals are intentionally classification-only. The P8A contract forbids detectors
from placing typed text, document contents, URLs or clipboard payloads into context
records.

Each detector has an ID and deterministic order. Detector exceptions are isolated;
one optional detector cannot stop base application automation.

P8A also preserves the focus HWND hint and explicit trigger reason:

- `ForegroundChanged`
- `FocusChanged`
- `ManualRefresh`
- `SystemRecovery`
- `RuleChanged`

This is required for later UI Automation and game-chat adapters.

## 3. Decision engine

`InputDecisionEngine` separates **what should happen** from **how the provider makes
it happen**.

Deterministic priority contract:

1. matching context policy;
2. application rule;
3. global default;
4. no target.

Context-policy ties are resolved by:

1. policy priority;
2. match specificity;
3. policy ID, ordinal ascending.

A context policy may return `Keep`. `Keep` is an explicit veto and does not fall
through to an application rule or global default.

Policy failures are isolated and traced. P8A registers no production context
policies, so this layer is behavior-neutral until P8B+.

## 4. Manual override gate

Manual override is a runtime veto **after** decision resolution. It is deliberately
not persisted as a rule.

P8A contract:

- default duration: 20 seconds;
- scope: the current top-level foreground HWND + owning process ID;
- leaving that foreground window clears the override immediately;
- HWND reuse by another process cannot inherit the previous override;
- transient AUMID/PFN lookup failure inside the same HWND/process does not cancel it;
- re-entering the application restores normal rule behavior;
- a matching automatic target is not suppressed;
- a conflicting automatic target is suppressed;
- `Keep`/no-target decisions are never turned into mutations.

P8A exposes an explicit `ReportManualInputOverride` hook but does not guess that an
arbitrary state difference was user intent. Automatic inference is deferred until a
verified TSF/input-language notification source is added. This avoids false manual
overrides caused by provider settling or another application's state.

This hook is ready for the future tray menu, hotkey integration, or a native input
state observer.

## 5. Decision journal / "why did FlowIME switch?"

`AutomationDecisionJournal` is an in-memory bounded ring buffer (128 records by
default). It records only decision metadata:

- timestamp/generation/trigger;
- HWND;
- privacy-bounded application key and process name;
- active context signal kinds;
- decision source/reason;
- provider/action;
- outcome;
- application-rule ID or context-policy ID;
- backend error code when applicable.

It explicitly excludes:

- window titles;
- full executable paths;
- typed text;
- URLs;
- clipboard data.

Current diagnostics now expose journal count, last outcome/reason, current stable
application identity, context signal kinds and current decision source/reason.

Outcomes currently include:

- `NoAction`
- `SuppressedByManualOverride`
- `Applied`
- `ApplyFailed`

## 6. UI status projection

`ForegroundContextService` now runs the same `InputContextEngine` and
`InputDecisionEngine` instances as automation. The Home page therefore cannot drift
from the actual decision path once context policies are enabled later.

`RuleResolutionSource` gains a `ContextPolicy` projection for UI status. No P8A
production path emits it because no context policies are registered yet.

## 7. Preserved invariants

P8A must preserve all of these:

- last-event-wins generation/cancellation;
- re-read actual foreground before mutation;
- bounded retry behavior;
- FlowIME process exclusion;
- pause/resume execution gate;
- P4 resume/unlock recovery;
- application rule wins over global default;
- `Keep` does not read/mutate provider state;
- existing rules.json remains readable without migration;
- Microsoft Pinyin / WeChat provider mutation semantics are unchanged;
- no polling loop, process injection, key logging, or administrator requirement.

## 8. Automated coverage added

P8A adds tests for:

- AUMID/PFN/path/executable-name application identity precedence;
- WindowsApps version changes producing the same stable identity;
- unpackaged same-name executables in different paths not colliding;
- base application context signal and focus HWND preservation;
- detector failure isolation;
- context policy overriding application rules;
- context `Keep` veto semantics;
- deterministic policy priority/specificity;
- preservation of application-rule/global-default semantics;
- manual override suppression, matching-target behavior, expiry and foreground exit;
- bounded newest-first decision journal;
- coordinator-level context-policy injection without coordinator branching;
- coordinator-level manual-override suppression and reset on foreground exit;
- packaged rule replacement across versioned WindowsApps paths;
- AUMID-based rule persistence, matching and rule upsert;
- AUMID/PFN propagation through WindowResolver and RunningApplicationCatalog;
- JSON round-trip of the optional AUMID match field without a schema migration.

## 9. Windows acceptance gate

From the repository root:

```powershell
dotnet test -c Debug
dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj -c Debug
```

Real-machine acceptance:

1. Existing Codex/Notepad/global-default tests behave exactly as P7.2.2.
2. Existing packaged-app rules still load and render.
3. Create/update a rule for Codex. Confirm rules.json includes the existing
   `processPath`, plus `packageFamilyName` and/or `applicationUserModelId` when
   Windows exposes them.
4. Restart FlowIME and confirm the rule still matches.
5. Pause/resume automation and confirm the current rule reapplies normally.
6. Lock/unlock once and confirm system recovery remains functional.
7. Copy diagnostics and confirm it includes `currentApplicationIdentity`,
   `currentContextSignals`, `currentDecisionSource`, `decisionJournalCount`, and
   does not expose the current full executable path.

P8A is accepted only after the full existing test suite remains green on Windows.

## 10. P8B/P8C/P8D integration points

### P8B fullscreen/gameplay

Add a Windows fullscreen/game detector implementing `IInputContextDetector`, then a
policy implementing `IInputContextPolicy`. No provider/coordinator branch required.

### P8C tray quick rules

Use the current `InputContextSnapshot.Application` identity to describe the active
app. Tray actions can write/update the normal app rule and can call
`ReportManualInputOverride` when the user explicitly chooses a temporary mode.

### P8D browser address bar

Add a UI Automation detector that uses `FocusHwnd` plus accessibility focus to emit
`BrowserAddressBar` / `TextInput` signals. Add a policy above the application-rule
layer. The app/global rule remains the fallback when address-bar focus leaves.

### P8E game text entry

Emit `GameTextEntry` from reliable UIA adapters or game profiles. A higher-priority
text-entry policy can temporarily override the lower gameplay policy and naturally
fall back when the signal disappears.
