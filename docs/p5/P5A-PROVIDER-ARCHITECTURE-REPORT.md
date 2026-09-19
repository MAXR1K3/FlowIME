# FlowIME P5A Input Method Provider Architecture Report

## Status

**Implementation complete; Windows build/test gate must be run on the user's machine.**

P5A is an architecture-only phase. It does not add a second IME, does not change
rule persistence, and does not change the P1-P4 Microsoft Pinyin behavior.

## Frozen behavior

The following remain unchanged:

- Application rules still contain only `Keep`, `Chinese`, or `English`.
- Existing `rules.json` schema and files remain valid without migration.
- Microsoft Pinyin Chinese conversion mode remains `0x00000401`.
- Microsoft Pinyin English conversion mode remains `0x00000000`.
- Focus re-resolution, TSF profile guards, bounded settle/retry, generation
  cancellation, pause/resume, tray lifecycle, hardening, and recovery behavior are
  unchanged.
- `InputOperationResult.Backend` remains `Microsoft Pinyin / Focused IMM`.

## New provider boundary

```text
AutomationCoordinator / ForegroundContextService
                 |
                 v
        IInputMethodBackend
                 |
                 v
     ProviderInputMethodBackend
                 |
                 v
    InputMethodProviderRegistry
                 |
                 v
       IInputMethodProvider
                 |
                 v
      MicrosoftPinyinProvider
```

`MicrosoftPinyinBackend` remains as a compatibility facade for existing callers and
tests, but production composition now uses the registry and provider adapter.

## Provider contract

Each provider owns:

- stable provider identity;
- capability declaration;
- active-profile detection;
- provider-specific state reading;
- provider-specific activation/mutation;
- provider-specific settle/retry behavior.

Microsoft Pinyin declares:

- active profile detection: supported;
- profile activation: supported;
- mode read: supported;
- force Chinese: supported;
- force English: supported;
- post-activation settling: required.

The Microsoft-specific conversion values and TSF propagation behavior remain inside
`MicrosoftPinyinProvider`; they are not generalized to other providers.

## P5B boundary

P5B may add another provider (initial target: WeChat IME) and probe its capabilities.
It must not assume that Microsoft Pinyin conversion values, IMM behavior, or settle
semantics apply to the new provider.

P5B may later extend rules with an explicit provider id, but P5A intentionally does
not change persistence. The provider registry's stable ids are the future persistence
boundary.

## Validation

Run on Windows from the repository root:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\p0-check.ps1
```

Then run the same real-machine P4 smoke matrix:

1. Codex Chinese / Notepad English foreground switching.
2. Manually invert a target mode, leave the app, return, and verify correction.
3. Rapid Alt+Tab.
4. Focus changes inside an application.
5. Pause/resume.
6. Close-to-tray / reopen / explicit quit.
7. Lock/unlock and sleep/resume.

P5A passes only if these behaviors are indistinguishable from the P4 accepted build.
