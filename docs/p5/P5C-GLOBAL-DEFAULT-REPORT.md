# FlowIME P5C Global Default / Fallback Report

**Stage:** P5C — candidate implementation pending Windows build and real-machine acceptance

P5B-3 established stable production switching between Microsoft Pinyin and
WeChat Input Method. P5C changes only rule resolution and configuration: it
adds one optional global default target for applications that have no enabled
matching application-specific rule.

## Frozen precedence contract

Rule resolution is deliberately one-shot:

```text
matching enabled application rule
        ↓ yes
use that rule, including explicit Keep

        ↓ no
configured global default
        ↓ yes
use global provider + Chinese/English target

        ↓ no
leave input state alone
```

The global default is a **match fallback**, not an execution fallback. If an
explicit application rule resolves but its provider later fails to activate or
mutate, FlowIME retries that same explicit target and then fails closed. It
must never execute the global default as a second choice.

Additional semantics:

- application rule > global default > no action;
- an explicit `Keep` application rule suppresses the global default;
- a disabled application rule does not match, so the global default may apply;
- pausing automation pauses application rules and the global default together;
- unlock/resume recovery re-evaluates the same precedence chain;
- FlowIME's own process is ignored by execution, so opening Settings never
  applies the global default to the FlowIME window;
- foreground events from FlowIME are still observed so they can cancel stale
  generations from another process.

## Persistence contract

The existing rules document remains **schema v2**. P5C adds an optional
`defaultTarget` member:

```json
{
  "schemaVersion": 2,
  "rules": [],
  "defaultTarget": {
    "providerId": "wechat-input-method",
    "action": "Chinese"
  }
}
```

`defaultTarget = null` means the global default is disabled.

The global default may only request `Chinese` or `English`. `Keep` is rejected
because a global Keep target would misleadingly appear to select a provider
while intentionally performing no mutation.

Application rules and the global default are persisted in one atomic document,
covered by the P4 primary/last-good write and recovery mechanism. Behavior
decisions consume one `RuleConfigurationSnapshot` captured under the
repository lock so rules and fallback cannot be observed from different
configuration revisions.

### Compatibility note

P5B-3 already owns schema v2 and ignores unknown JSON members. It can therefore
read a P5C document without misrouting an application rule; it simply does not
implement `defaultTarget`. If an older P5B-3 build subsequently saves rules, it
may drop the P5C-only global-default setting. This is a safe loss of the
fallback preference, not a provider misexecution.

## UI contract

The Rules page contains a fixed **Global Default Scene** card above
application-specific rules. Editing it allows:

- enable / disable global default;
- select a registered provider;
- select Chinese or English.

The Home page identifies whether the current resolved target came from an
application rule or the global default.

## Automated coverage added

P5C adds or extends tests for:

- unmatched application resolves to the global default;
- application rule overrides the global default;
- explicit Keep suppresses the global default;
- disabled rule allows the global default;
- explicit rule execution failure never falls back across providers;
- FlowIME's ignored process never receives the global default;
- global default persistence round-trip and last-good backup;
- application-rule writes preserve the global default;
- global-default writes preserve application rules;
- disabling the default leaves application rules intact;
- invalid global Keep is rejected;
- foreground status reports `GlobalDefault` resolution;
- Rules/Home UI projects and edits the fallback target.

## Frozen provider boundary

P5C must not modify the native provider algorithms validated in P5B-3.
Pre-delivery hash comparison covers at least:

- `MicrosoftPinyinProvider.cs`;
- `WeChatInputMethodProvider.cs`;
- WeChat TSF activation/profile identity;
- the Microsoft Pinyin compatibility backend.

## Windows build gate

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\p0-check.ps1
```

The sandbox used to produce this candidate has no .NET SDK, so static source
and XAML checks are not a substitute for this Windows build/test gate.

## Real-machine acceptance matrix

Recommended first configuration:

```text
Global default -> WeChat Input Method -> Chinese
Notepad        -> Microsoft Pinyin     -> English
```

Validate:

1. an unmatched application becomes WeChat Chinese;
2. Notepad remains Microsoft Pinyin English and overrides the default;
3. an explicit Keep rule preserves the current input state rather than using
   the default;
4. disabling an application rule causes that application to use the default on
   the next foreground re-evaluation;
5. opening FlowIME itself does not apply the global default;
6. pause/resume pauses and restores both application and fallback behavior;
7. lock/unlock and sleep/resume re-apply the correct target;
8. rapid Alt+Tab does not let an older application/default generation win.

**Current decision:** P5C CANDIDATE — pending Windows `p0-check` and real-machine acceptance.
