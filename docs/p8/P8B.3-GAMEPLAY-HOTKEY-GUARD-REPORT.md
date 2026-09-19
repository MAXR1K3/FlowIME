# P8B.3 — Gameplay Hotkey Guard

## Goal

Prevent accidental Windows/IME language-switch shortcuts from changing input state while a positively identified Gameplay context is active. This phase deliberately does **not** add a Gameplay input policy or force an IME profile/mode; it solves the earlier, more fundamental problem of unintended shortcut activation.

## Runtime gate

The guard is armed only when all four conditions are true:

1. user setting `Enabled=true`;
2. FlowIME automation is enabled;
3. current context contains `Game`;
4. current context does **not** contain `GameTextEntry`.

Every top-level foreground transition disarms the guard immediately. The P8 context pipeline may re-arm it after the new foreground window is positively classified as Gameplay. This prevents stale Game state from leaking into FlowIME itself or another desktop application.

## Shortcut coverage

Default:

- `Win + Space` — blocked;
- `Ctrl + Space` — blocked;
- `Alt + Shift` / `Ctrl + Shift` — available but disabled by default because these chords may also be legitimate game binds.

The hook suppresses only the terminal key of a recognized configured chord. It does not record typed characters, window titles, URLs, or arbitrary key sequences.

## Windows implementation

`FlowIME.Windows.Input.GameplayHotkeyGuard` uses `WH_KEYBOARD_LL` on a dedicated native-message-loop thread. The callback performs only bounded in-memory modifier/chord matching and immediately returns. Injected input is ignored.

A pure `GameplayHotkeyChordMatcher` and `GameplayHotkeyGuardGate` keep chord/gating policy unit-testable without installing a native hook.

## Settings

Gameplay hotkey preferences are persisted independently in `%LOCALAPPDATA%\\FlowIME\\settings.json` through `JsonAppSettingsRepository`. Rule persistence remains isolated in `rules.json`, so a preferences-file problem cannot invalidate application rules or their last-known-good recovery path.

## UI

Settings > 游戏保护 exposes:

- master Gameplay shortcut guard;
- Win + Space;
- Ctrl + Space;
- optional legacy Alt + Shift / Ctrl + Shift.

The section uses the existing responsive layout states.

## Diagnostics

The diagnostics snapshot now includes:

- hook installed / armed;
- current Game and GameTextEntry gate states;
- each configured shortcut family;
- suppression count;
- last suppressed chord and timestamp;
- current process label used by the gate;
- hook/runtime error.

No keystroke content is logged.

## Explicit non-goals

P8B.3 does not:

- force English in Gameplay;
- switch provider/profile when Game begins;
- detect an in-game chat box yet;
- add per-game override UI yet;
- inject synthetic keyboard input.

The existing Gameplay apply failure discovered in P8B.2 remains independently diagnosable. A later Gameplay input policy should be designed only after the execution path is understood.
