# P8C.2 — GameTextEntry Detection and Per-Game Chat Profiles

## Goal

Turn the P8C.1 GameTextEntry state model into a usable feature without weakening the
validated P8B Gameplay safety model. P8C.2 adds two conservative activation paths:

1. standard editable-control focus detection for games that expose Windows accessibility;
2. explicit per-game chat-key profiles for self-rendered game UIs.

A detector cannot create GameTextEntry outside a confirmed `Game` context.

## Standard text-control detection

`StandardGameTextEntryDetector` is opt-in per game and only probes while that
configured game's top-level window is geometrically fullscreen. It uses a content-free
Windows accessibility probe:

- EVENT_OBJECT_FOCUS metadata is propagated into `ContextDetectionRequest`;
- MSAA `IAccessible` role/state may classify a focused editable text control;
- native Edit/RichEdit class names are a fallback;
- typed text, accessible names/values, document contents, URLs and clipboard data are
  never read.

Only High/Certain evidence is allowed to establish `GameTextEntry`. Generic Document
containers are considered ambiguous and fail closed. A positive focus observation is
latched in `GameTextEntryRuntimeState`; a later non-text focus observation removes it.
That latch lets post-apply/manual refreshes preserve the text-entry state without
repeating cross-process accessibility calls.

## Explicit hotkey profiles

Many games render chat/search UI themselves and expose no standard edit HWND. P8C.2
therefore adds `GameTextEntryHotkeyMonitor`, a separate `WH_KEYBOARD_LL` observer.

The monitor:

- is armed only while automation is enabled, the foreground context is `Game`, and a
  matching enabled profile opts into `HotkeyProfile`;
- observes only user-configured enter/exit gestures;
- ignores injected input;
- suppresses key auto-repeat for state transitions;
- never consumes the original key and always continues the hook chain;
- schedules state transitions outside the hook callback.

The opening key's key-up is explicitly ignored as an exit candidate so a profile using
Enter for both open and submit cannot immediately close itself.

## Gesture contract

The human-editable gesture syntax supports common keys and exact modifier sets, for
example:

- `Enter, T, Y`
- `Enter, Esc`
- `Ctrl+Y`
- `F2`
- `Slash`

The parser canonicalizes and de-duplicates gestures. Hotkey profiles require at least
one enter and one exit gesture.

## Profile persistence

Profiles are intentionally stored separately from application rules and app settings:

`%LOCALAPPDATA%\FlowIME\game-text-entry-profiles.json`

The repository uses schema version 1, validation through the Core registry, an atomic
temporary-file replacement, and fail-safe empty loading for malformed/unknown schema
files. A bad game profile file therefore cannot block normal FlowIME startup or damage
`rules.json`.

## UI

Settings now contains a `Game Text Input` card. FlowIME remembers the most recently
confirmed Gameplay application and lets the user configure that game after returning
to the app.

A profile controls:

- enabled/disabled state;
- standard-control detection;
- hotkey-profile detection;
- enter gestures;
- exit gestures;
- text-entry input target.

The default target is `Keep`, deliberately avoiding an automatic IME mutation before
the state detector itself has been validated for the game. Provider Chinese/English
targets can then be selected explicitly.

## State transitions

For a hotkey profile:

`Gameplay (US)` -> configured open key -> `GameTextEntry` -> configured submit/cancel
key -> `Gameplay (US)`.

On entry, the existing P8C seam immediately relaxes Gameplay US baseline and hotkey
guard before the asynchronous context/decision pass. On exit, the ordinary context
refresh restores the Gameplay baseline and guard.

For a standard text control, focus entering a high-confidence editable control latches
GameTextEntry and focus leaving it clears that owned session. Non-focus refreshes reuse
the runtime latch instead of probing the external game again.

## Privacy and anti-cheat boundary

P8C.2 does not use OCR, screen capture, game memory, process injection, DLL injection or
input simulation. The accessibility path reads classification metadata only. The
hotkey path observes configured trigger keys but does not log ordinary typed content
and does not consume the game key.

## Diagnostics

Diagnostics add hotkey monitor state/counters and profile-file existence. Existing
`currentContextSignalDetails` exposes standard detector sources, while runtime fields
show adapter-driven hotkey sessions.

## Validation target

P8C.2 must preserve the P8B regression contract before profile configuration:

- entering a confirmed game -> US baseline;
- Gameplay hotkey guard remains armed;
- overlay remains correct;
- Alt+Tab restores the destination application's rule/global default.

After an explicit profile is created, only that game gains GameTextEntry behavior.
