# P8C.1 — GameTextEntry Context Foundation

## Goal

Introduce a first-class `GameTextEntry` child context without automatically trying to
recognize chat boxes yet. P8C.1 must keep the validated P8B lifecycle unchanged:

`Gameplay -> US keyboard baseline -> hotkey guard -> Gameplay exit restore`.

No production adapter automatically activates GameTextEntry in this phase.

## Context invariant

`GameTextEntry` is valid only while `Game` is also present. `InputContextEngine`
normalizes detector output so a stale/incorrect GameTextEntry signal outside Gameplay
is removed. A confirmed GameTextEntry also derives generic `TextInput`, allowing later
browser/game/text policies to share one semantic signal.

## Runtime session state

`GameTextEntryRuntimeState` is the adapter-facing latch for a future detector. It stores
only:

- stable application identity key;
- PID / top-level HWND;
- process label;
- activation source;
- optional profile ID;
- timestamps and a bounded reason token.

It never stores typed text, chat content, accessibility values, URLs or clipboard data.
The state is cleared immediately when the top-level foreground changes, automation is
paused, or Gameplay ceases to be active.

## Passive detector

`GameTextEntryRuntimeDetector` projects the runtime latch into the existing P8 context
engine. It performs no UI Automation, keyboard observation or game-specific probing.
That makes the P8C.1 production default behavior identical to P8B.4.3.

## Profile contract

P8C.1 defines an in-memory `GameTextEntryProfile` contract for later persistence/UI:

- stable application identity key;
- detection mode flags (`StandardTextControl`, `HotkeyProfile`, `Manual`, `Adapter`);
- input target (`provider + Chinese/English/Keep`);
- enter / exit key gestures for future game profiles.

`GameTextEntryProfileRegistry` uses replace-all immutable snapshots for deterministic,
thread-safe reads. P8C.1 starts with an empty registry and deliberately does not modify
`rules.json` or `settings.json`.

## Decision semantics

`GameTextEntryPolicy` has higher priority than the Gameplay US baseline.

- no `GameTextEntry` -> policy does nothing;
- `GameTextEntry` + matching profile -> profile target;
- `GameTextEntry` + no profile -> explicit `Keep` fail-closed behavior.

The last case is important: an experimental/future detector cannot accidentally fall
through to an unrelated application rule or global default merely because the US
baseline is suspended for text entry.

## Existing P8B integration

P8B already treated `GameTextEntry` as the boundary for:

- suspending the US-keyboard baseline;
- disarming the Gameplay hotkey guard;
- changing overlay persistence behavior.

P8C.1 now gives those hooks a real state source while leaving it inactive by default.

## Adapter-facing seam

`AppServices` exposes foundation methods for later adapters:

- `TryEnterGameTextEntry(...)` — accepted only while current context is Gameplay;
- `ExitGameTextEntry(...)`;
- `ReplaceGameTextEntryProfiles(...)`.

On accepted entry, the Gameplay US baseline and hotkey guard are relaxed immediately
before the asynchronous `GameTextEntryChanged` context refresh. This prevents a future
adapter from racing the existing Gameplay protections while it is trying to enter a
text-input state. No current UI or automatic component calls `TryEnterGameTextEntry`,
so the validated P8B behavior is unchanged.

## Diagnostics

Diagnostics add only state metadata:

- active / generation;
- activation source;
- profile ID;
- process label;
- activation/change timestamps;
- last reason;
- in-memory profile count.

No user-entered text is captured.

## Next phase

P8C.2 should implement the first real adapter in two conservative layers:

1. standard UI Automation text-control focus detection where the game exposes a real
   Edit/Document control;
2. explicit per-game hotkey profiles for self-rendered chat UIs.

Automatic visual/OCR/game-memory detection is intentionally out of scope for the
foundation and should not be required for a safe first release.
