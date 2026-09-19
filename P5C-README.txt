FlowIME P5C candidate — Global Default / Fallback

Adds an optional global default provider + Chinese/English target used only
when no enabled application-specific rule matches.

Frozen semantics:
- application rule > global default > no action
- explicit Keep suppresses global default
- disabled application rule allows global default
- application-rule execution failure never falls back to global default
- FlowIME's own process is excluded
- P5B-3 provider mutation implementations are unchanged

Build/test on Windows:
  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
  .\scripts\p0-check.ps1

See docs\p5\P5C-GLOBAL-DEFAULT-REPORT.md.
