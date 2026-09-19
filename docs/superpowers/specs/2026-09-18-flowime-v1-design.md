# FlowIME V1 Design

## Goal

FlowIME V1 is a Windows 11 x64 tray application that applies a Microsoft Pinyin input-state rule when the foreground application changes.

User-facing actions are `Keep`, `Chinese`, and `English`. `English` means Microsoft Pinyin's English/alphanumeric state, not automatically switching to a US keyboard layout.

## Core behavior

Rules execute **on entry**. If a user manually changes the IME state while staying in an application, FlowIME does not fight the user. Leaving and re-entering the application applies the rule again.

Foreground changes are event-driven through `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`. A short debounce resolves the final HWND. Before applying a stale asynchronous operation, FlowIME must verify that the target is still foreground.

## Architecture

- `FlowIME.Core`: domain types, rule engine, state machine, abstractions. No Win32/WinUI dependencies.
- `FlowIME.Windows`: Win32, IMM32 and TSF integrations, window identity, Microsoft Pinyin backend.
- `FlowIME.Infrastructure`: persisted configuration and logging.
- `FlowIME.App`: later WinUI 3 shell.
- `FlowIME.Probe`: P0 diagnostics and backend feasibility CLI.

## P0 gate

Before UI development, prove on Windows 11 that a normal-privilege process can:

1. Resolve foreground HWND/process/thread identity.
2. Read the target thread keyboard layout.
3. Read Microsoft Pinyin open/close state without simulated keystrokes.
4. Set the target state to open/closed without simulated keystrokes.
5. Verify behavior in Notepad, Explorer, Chrome/Edge, VS Code, Windows Terminal and WeChat.
6. Document elevated-process limitations.
7. Inspect the active TSF keyboard profile and local TSF compartment values without confusing local thread-manager state with target-process state.

If native mutation is unreliable in core apps, stop before UI development and design a TSF/native bridge instead of hiding the failure behind keyboard simulation.

## Privacy and safety

FlowIME does not capture typed text, clipboard contents, browser page contents, or target process memory. It does not inject DLLs. Default operation is non-elevated.
