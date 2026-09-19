FlowIME P4 — Reliability Hardening

This build layers P4 on top of the accepted P3 baseline.

Added:
- rules.json validated temp-write + latest last-good backup and recovery
- bounded corrupt-artifact retention and stale-temp cleanup
- runtime automation.log rotation (2 MiB + 3 archives)
- sleep/resume and session-unlock recovery through the resident tray HWND
- delayed current-foreground reapply after system recovery
- copyable diagnostics snapshot in Settings

Not changed:
- Microsoft Pinyin 0x401 / 0x0 semantics
- P1 generation / retry / focus backend
- P2 single-instance / close-to-tray / startup behavior
- P3 rule schema and rule-management semantics
- no third-party IME provider yet

Run scripts\p0-check.ps1 before real-machine P4 acceptance.
See docs\p4\P4-HARDENING-REPORT.md.
