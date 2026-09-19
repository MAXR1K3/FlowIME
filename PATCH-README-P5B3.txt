FlowIME P5B-3 candidate production integration

Base: P5B-2 / P5A production behavior + P4 hardening.

What changes:
- production WeChatInputMethodProvider
- per-rule provider selection
- rules schema v2 with automatic v1 -> Microsoft Pinyin migration
- provider-aware current-state detection and rule UI

Validated evidence before this build:
- WeChat Chinese = IMM OpenStatus Open
- WeChat English = IMM OpenStatus Closed
- both directions passed native read-back and manual typing in Notepad

Still requires real-machine candidate validation:
- Microsoft Pinyin -> WeChat TSF profile activation through the production path
- WeChat -> Microsoft Pinyin transition
- Codex/Electron and Chrome/Edge host behavior

Install:
1. Exit FlowIME from the tray.
2. Extract this patch into C:\FlowIME and overwrite files.
3. Run .\scripts\p0-check.ps1.
4. Start FlowIME and create rules choosing both the input method and Chinese/English.

If a WeChat rule fails, do not manually correct it until you save the tail of:
%LOCALAPPDATA%\FlowIME\logs\automation.log
