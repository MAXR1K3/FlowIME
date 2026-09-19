# FlowIME P8B.2.1 — Gameplay Apply Diagnostics

## Why this checkpoint exists

P8B.2 successfully classified a real game (`Resonance`) as
`Application,Fullscreen,Game`, but the existing global-default mutation path
returned `ApplyFailed` while that game was foreground. Enabling a Gameplay
English policy before understanding that failure would only change the requested
mode, not fix the execution path.

## Change

This checkpoint is diagnostics-only. It does not register a gameplay policy and
does not modify provider behavior. Recent decision diagnostics now append:

```text
provider=<provider-id>|action=<InputAction>|error=<error-code>
```

Example:

```text
recentDecision[0]=...|Resonance|Application,Fullscreen,Game|...|ApplyFailed|provider=wechat-input-method|action=Chinese|error=focus-unavailable
```

The decision journal already stored these fields; P8B.2.1 only exposes them in
the privacy-bounded copied diagnostics. No window title, executable path, URL,
typed text, or clipboard content is added.

## Gate before P8B.3

Capture at least one failed gameplay mutation with the concrete error code.
Then choose the Gameplay English executor based on evidence:

- `focus-unavailable`: current focused-IMM path is structurally unavailable in
  that gameplay state; do not simply retry it under a new policy.
- `read-failed` / `write-failed`: investigate the game's IMM context semantics.
- `profile-activation-failed`: investigate TSF profile activation separately.
- settling/verification errors: tune provider transition sequencing only after
  reproducing the exact failure.

P8B.3 should not be enabled until this gate is resolved.
