FlowIME P5A — IME Provider architecture

This phase is intentionally behavior-preserving.

New architecture:
  IInputMethodProvider
    -> InputMethodProviderRegistry
    -> ProviderInputMethodBackend
    -> MicrosoftPinyinProvider

MicrosoftPinyinBackend remains as a compatibility facade.

No rule schema migration is required. Existing rules.json remains valid.
No third-party IME support is enabled yet.

Windows validation:
  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
  .\scripts\p0-check.ps1

After the gate is green, run the same P4 real-machine smoke tests before starting P5B.
