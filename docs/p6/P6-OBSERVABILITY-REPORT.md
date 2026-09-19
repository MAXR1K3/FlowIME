# FlowIME P6 Rule Observability and Health Report

## Scope

P6 deliberately does not change input-provider activation or mutation behavior.
Microsoft Pinyin continues to own ConversionMode semantics and WeChat Input Method
continues to own OpenStatus semantics exactly as in the P5B-3 validated baseline.
The global-default resolution semantics from P5C are also unchanged.

P6 adds two observational/product layers:

1. Home-page rule resolution visibility: the UI separately shows the resolution
   source (application rule / global default / unmatched) and the resolved target.
2. Non-mutating rule-set health diagnostics: exact duplicate match groups are
   detected and classified as conflicting targets or redundant targets.

## Rule health semantics

Diagnostics consider enabled application rules only. Two rules are an exact overlap
only when all normalized match fields are identical. A process-wide rule and a
more-specific window-title rule are therefore not classified as a conflict.

- Same match + different target => conflicting-target group.
- Same match + same target => redundant-target group.
- Disabled rules do not participate.
- `Keep` ignores ProviderId for target comparison because it does not mutate an IME.

The analyzer never rewrites priorities, deletes rules, changes matching, or changes
automation. Runtime resolution remains owned by `RuleEngine`.

## Diagnostics snapshot

The copyable diagnostics report now includes exact-overlap, conflicting-target,
redundant-target, and affected-rule counts. This is safe metadata only and does not
include typed text or window titles.

## Validation gate

Run on Windows from the repository root:

```powershell
.\scripts\p0-check.ps1
```

Then verify the Home page distinguishes application-rule / global-default / unmatched
sources and that a healthy normal configuration shows no warning InfoBar on Rules.
