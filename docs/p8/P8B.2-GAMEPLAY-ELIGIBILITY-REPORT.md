# FlowIME P8B.2 — Gameplay Eligibility

## Goal

P8B.2 separates **fullscreen presentation** from **gameplay classification**.
A fullscreen window is not automatically a game: browser F11, video playback,
presentations and remote-desktop sessions can all occupy a monitor.

This phase is observation/classification only. It does **not** register a gameplay
input policy, so the selected IME behavior remains the same as P8B.1.1.

## Runtime pipeline

```text
Foreground / Focus
  -> WindowResolver
  -> InputContextEngine
       -> FullscreenWindowDetector
       -> GameplayEligibilityDetector
            -> geometric fullscreen prerequisite
            -> Windows GameConfigStore evidence (read-only, optional)
            -> Shell Direct3D-exclusive evidence (documented SHQueryUserNotificationState)
            -> GameplayEligibilityEvaluator
  -> InputDecisionEngine (unchanged; no gameplay policy yet)
  -> ManualOverrideGuard
  -> Provider backend
```

## Classification contract

Automatic `Game` classification requires geometric fullscreen plus at least one
strong game fact:

1. `WindowsGameMetadata`
   - exact executable-path match in the current user's
     `System\\GameConfigStore\\Children` metadata;
   - read-only and optional;
   - positive result cached for 10 minutes, negative result for 10 seconds.

2. `Direct3DExclusive`
   - Shell reports `QUNS_RUNNING_D3D_FULL_SCREEN`;
   - `QUNS_BUSY` is deliberately **not** considered game evidence because it also
     covers ordinary fullscreen applications/presentation scenarios.

One strong fact -> `Game` with High confidence.
Both independent facts -> `Game` with Certain confidence.

The pure evaluator already reserves two future evidence kinds:

- `UserDeclaredGame` -> authoritative game classification;
- `UserDeclaredNotGame` -> hard veto.

These are intentionally modeled now so later UI/tray overrides do not require a
new classifier contract.

## Privacy boundary

Gameplay diagnostics may contain:

- process name;
- fullscreen boolean;
- eligibility boolean;
- confidence;
- semantic reason/evidence kind.

They do not contain:

- window title;
- executable path;
- URL;
- typed text;
- clipboard content.

The local executable path is used transiently to compare against Windows game
metadata but is never copied into the gameplay observation history.

## Diagnostics

P8B.2 adds:

```text
currentContextSignalDetails=...
recentGameplayCount=N
recentGameplay[0]=timestamp|process|fullscreen=True|eligible=...|confidence=...|reason=...|evidence=...
```

Only fullscreen eligibility attempts are retained in `recentGameplay`, preventing
ordinary FlowIME/Explorer focus changes from pushing the interesting sample out of
the short diagnostic window.

Expected browser F11 example:

```text
recentDecision[..]=...|chrome|Application,Fullscreen|...
recentGameplay[..]=...|chrome|fullscreen=True|eligible=False|...|reason=insufficient-game-evidence|evidence=none
```

Expected detected game example:

```text
recentDecision[..]=...|game|Application,Fullscreen,Game|...
recentGameplay[..]=...|game|fullscreen=True|eligible=True|confidence=High|reason=windows-game-metadata|evidence=WindowsGameMetadata
```

## Deliberate non-goals

P8B.2 does not:

- force English in games;
- change application/global rule priority;
- detect game chat/text entry;
- expose a user-facing "this is a game" toggle yet;
- treat generic fullscreen (`QUNS_BUSY`) as gameplay;
- mutate Windows GameConfigStore.

The next policy phase should only be enabled after real-game eligibility is verified
on representative fullscreen and borderless-fullscreen titles.
