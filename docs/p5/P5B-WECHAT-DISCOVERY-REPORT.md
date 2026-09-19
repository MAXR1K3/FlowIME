# FlowIME P5B WeChat IME Discovery Report

**Stage:** P5B-1 — read-only capability discovery

P5A froze the provider abstraction while keeping Microsoft Pinyin behavior unchanged. P5B begins with evidence collection for WeChat Input Method. No `WeChatImeProvider` is permitted until this matrix establishes what the installed WeChat IME exposes on the user's real Windows 11 system.

## Safety boundary

P5B-1 is read-only.

It may read:

- current foreground/focus HWNDs;
- target thread keyboard layout;
- `ImmGetDefaultIMEWnd` + `IMC_GETOPENSTATUS`;
- `IMC_GETCONVERSIONMODE`;
- the active TSF keyboard profile identity.

It must **not**:

- send `IMC_SETOPENSTATUS` to WeChat Input Method;
- send `IMC_SETCONVERSIONMODE` to WeChat Input Method;
- reuse Microsoft Pinyin `0x401 / 0x0` constants;
- activate an assumed WeChat TSF profile from guessed CLSID/GUID values;
- advertise read/set capabilities for WeChat before real-machine validation.

## Build gate

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\scripts\p0-check.ps1
```

P5B adds `FlowIME.Probe.Tests`; all existing P1–P5A tests must remain green.

## First discovery matrix

Use Notepad for the first matrix so the target host is simple and repeatable.

Run:

```powershell
.\scripts\p5b-wechat-probe.ps1
```

The script performs two read-only captures:

1. WeChat Input Method visibly in **Chinese** mode.
2. The same input method and same text host visibly in **English** mode.

Each capture samples five times and requires the target top-level window to remain foreground. Captures are saved to:

```text
%LOCALAPPDATA%\FlowIME\probe\wechat-chinese.json
%LOCALAPPDATA%\FlowIME\probe\wechat-english.json
```

The script then compares the files automatically.

## Decision matrix

| Observation | P5B interpretation | Next experiment |
| --- | --- | --- |
| Same TSF profile; stable conversion modes differ | Conversion mode is a candidate provider-owned state | Add guarded raw write/read-back experiment using only the observed WeChat profile identity and observed values |
| Same TSF profile; open status differs | IMM open status is a candidate provider-owned state | Add guarded open-status write/read-back experiment |
| Both differ | Both signals are candidates | Prefer the narrower signal after host-matrix verification |
| No IMM signal differs | Legacy IMM cannot currently distinguish WeChat Chinese/English | Investigate provider-specific TSF compartments/native API; do not fake support |
| TSF profile differs | The test changed input profiles rather than an internal WeChat mode | Repeat the capture correctly |
| Values vary within a capture | Context had not settled or the host exposes unstable state | Repeat with a longer delay and then test another host |

## Required host matrix before mutation becomes production code

After Notepad identifies a candidate signal, repeat the same read-only matrix in at least:

- Codex / Electron;
- Chrome or Edge / Chromium;
- Windows Terminal or another modern text host.

A WeChat provider may only advertise a read capability if the same semantic mapping holds across the supported host matrix.

## Provider acceptance levels

P5B can legitimately finish at different capability levels:

### Level 1 — profile only

- identify WeChat profile: PASS
- activate WeChat profile: PASS
- read Chinese/English: unsupported
- force Chinese/English: unsupported

### Level 2 — readable mode

- identify/activate: PASS
- read Chinese/English: PASS
- force Chinese/English: unsupported

### Level 3 — full provider

- identify/activate: PASS
- read Chinese/English: PASS
- set Chinese: PASS with read-back
- set English: PASS with read-back

FlowIME must report the real capability level rather than pretending unsupported mutations are reliable.

## Current decision

**P5B-1 READ-ONLY PASS — PENDING P5B-2 OPEN-STATUS MUTATION VALIDATION**

## P5B-2 mutation candidate

The user's first read-only matrix produced a stable distinction:

- TSF profile: same for Chinese and English
- CLSID: `{86598FB9-66A2-463E-B9C2-AEB906D477AD}`
- Profile GUID: `{607FDF85-FCC8-4DBD-A365-41296F980C9C}`
- Language: `0x0804`
- IMM conversion mode: `0x00000001` in both states
- IMM open status: `Open` for the observed Chinese state, `Closed` for the observed English state

This makes `IMC_SETOPENSTATUS` the next mutation candidate. It is **not** yet a production provider contract.

`FlowIME.Probe set-open` therefore has stronger safety gates than the older generic probe commands:

1. the target must still be foreground;
2. the active TSF profile must exactly match the observed WeChat identity above;
3. a real `hwndFocus`/`hwndCaret` input target must be available; top-level fallback is rejected;
4. the requested state must be different from the current state so the test exercises an actual transition;
5. after mutation, foreground, exact focused input HWND, and TSF profile must remain unchanged;
6. open status must match the request across multiple delayed read-back samples;
7. no text, shortcut, or simulated key input is sent;
8. manual visual/input confirmation in Notepad is still required.

Run:

```powershell
.\scripts\p5b-wechat-mutation.ps1
```

Do not implement `WeChatImeProvider` until both English -> Chinese and Chinese -> English pass native read-back **and** manual typing confirmation.

## P5B-2 result — PASS on Notepad

Real-machine validation confirmed both mutation directions with native read-back
and manual typing confirmation:

- Chinese: `IMC_GETOPENSTATUS = Open`
- English: `IMC_GETOPENSTATUS = Closed`
- conversion mode remains `0x00000001` in both states
- foreground retained
- focused input HWND retained
- exact WeChat TSF profile retained
- no simulated key input was used

The validated profile identity is:

```text
CLSID   {86598FB9-66A2-463E-B9C2-AEB906D477AD}
Profile {607FDF85-FCC8-4DBD-A365-41296F980C9C}
LANG    0x0804
```

**Decision:** P5B-2 PASS for OpenStatus mutation in Notepad.

## P5B-3 candidate production integration

P5B-3 adds `WeChatInputMethodProvider` and rule-level provider selection.
This is intentionally a candidate until the full application path validates two
additional facts that P5B-2 did not exercise:

1. FlowIME can activate the WeChat TSF profile from another active provider using
   `ITfInputProcessorProfileMgr.ActivateProfile` and wait for the target GUI
   thread's IME context to settle.
2. The Open/Closed semantic remains valid in the supported host matrix, especially
   Electron/Chromium hosts in addition to Notepad.

The provider fails closed when the exact profile cannot be detected/activated.
It never reuses Microsoft Pinyin conversion-mode semantics.

### P5B-3 real-machine acceptance

Create at least two rules so every transition is exercised:

```text
Notepad -> Microsoft Pinyin -> English
Codex   -> WeChat Input Method -> Chinese
```

Then test:

- Notepad -> Codex: profile changes Microsoft -> WeChat and Codex becomes Chinese;
- Codex -> Notepad: profile changes WeChat -> Microsoft and Notepad becomes English;
- manually flip WeChat to English inside Codex, leave and return: FlowIME restores Chinese;
- change the Codex rule to WeChat English and repeat;
- repeat a WeChat Chinese/English rule in Chrome or Edge;
- rapid Alt+Tab does not allow a stale provider operation to overwrite the latest rule;
- pause/resume and lock/unlock continue to work.

If profile activation fails, capture `%LOCALAPPDATA%\\FlowIME\\logs\\automation.log`
before manually correcting the IME.

**Current decision:** P5B-3 CANDIDATE — pending integrated cross-provider/host validation.

### Rule persistence migration

P5B-3 upgrades `rules.json` from schema v1 to v2 because rules can now persist a
provider ID. Existing v1 rules are read as `microsoft-pinyin` automatically.
New/updated documents are written as v2.

The version bump is a safety boundary: a pre-P5B FlowIME build must reject a v2
rule document rather than silently ignore `providerId` and apply a WeChat-targeted
rule through Microsoft Pinyin.

## P5B-3 real-machine result — PASS

The integrated production path was validated on the user's Windows 11 machine
with stable bidirectional switching between Microsoft Pinyin and WeChat Input
Method. The validation exercised real application rules rather than Probe-only
mutation:

- Microsoft Pinyin rules continued to use provider-owned conversion-mode state;
- WeChat rules used provider-owned OpenStatus state;
- switching from Microsoft Pinyin to WeChat and back was stable;
- the target provider and requested Chinese/English mode were both applied;
- the P5A/P5B compatibility tests were repaired without changing either
  production provider implementation.

**Decision:** P5B-3 PASS — multi-provider production switching is frozen as the
baseline for P5C. Future rule-resolution work must not change either provider's
native mutation algorithm.
