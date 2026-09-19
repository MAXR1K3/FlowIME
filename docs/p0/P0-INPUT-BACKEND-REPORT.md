# FlowIME P0 Input Backend Report

**Status:** NOT EXECUTED — requires a Windows 11 desktop session with Microsoft Pinyin installed.

This report is the hard gate between Tasks 1–7 and the production backend/UI work. Do not mark P0 as passed from source inspection alone.

## Environment

| Field | Value |
| --- | --- |
| Windows edition/build | Not recorded |
| Architecture | x64 expected |
| .NET SDK | Not recorded |
| Microsoft Pinyin version/profile | Not recorded |
| FlowIME commit | Not recorded |
| Probe privilege | Normal user |

## Build and unit-test gate

Run from the repository root in PowerShell:

```powershell
.\scripts\p0-check.ps1
```

Record:

| Check | Result |
| --- | --- |
| `dotnet restore` | NOT RUN |
| `dotnet build -c Debug` | NOT RUN |
| `dotnet test -c Debug` | NOT RUN |
| Warnings treated as errors | NOT VERIFIED |

## How to test a target application

1. Ensure Microsoft Pinyin is the target application's active input profile.
2. Run `dotnet run --project src/FlowIME.Probe -- watch` and switch to the application.
3. Copy the target HWND printed by the watcher.
4. Manually put Microsoft Pinyin into Chinese mode, then run `inspect --hwnd <HWND>` and record `KeyboardLayout`, `IMM open status`, TSF profile information, and what the UI visibly shows.
5. Manually put Microsoft Pinyin into English mode and repeat the inspection.
6. Run `set --hwnd <HWND> --mode chinese`; verify the target visibly becomes Chinese without receiving a character, shortcut, menu action, focus change, or other side effect.
7. Run `set --hwnd <HWND> --mode english`; verify the same for English.
8. Repeat at least three times to catch non-deterministic behavior.

The Probe deliberately calls the IMM result **Open/Closed**, not Chinese/English. Only the visual matrix below can establish whether those states are a valid Microsoft Pinyin mapping on the tested Windows build.

## Core application matrix

Use `PASS`, `FAIL`, or `N/A`, and add a short note for every failure.

| Application | Read Chinese | Read English | Set Chinese | Set English | Focus retained | No shortcut/text side effect | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Notepad | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | |
| File Explorer | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | |
| Chrome | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | |
| Edge | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | |
| VS Code | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | |
| Windows Terminal | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | |
| WeChat | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | |

## Foreground event validation

Run:

```powershell
dotnet run --project src/FlowIME.Probe -- watch
```

Switch repeatedly through Notepad → Chrome → Terminal → VS Code → Explorer.

| Check | Result |
| --- | --- |
| Every foreground transition produces the correct HWND/process | NOT RUN |
| No fixed-frequency polling is involved | SOURCE VERIFIED |
| Rapid Alt+Tab does not crash the watcher | NOT RUN |

## TSF observations

`inspect` prints two different categories of evidence:

- **Active keyboard profile:** obtained from `ITfInputProcessorProfileMgr::GetActiveProfile`.
- **Probe current-thread compartments:** `KEYBOARD_OPENCLOSE` and `INPUTMODE_CONVERSION` from FlowIME.Probe's own `ITfThreadMgr` only.

The local compartment values must **not** be treated as the target application's state.

| Observation | Chinese | English |
| --- | --- | --- |
| Active profile CLSID | NOT RUN | NOT RUN |
| Active profile GUID | NOT RUN | NOT RUN |
| HKL | NOT RUN | NOT RUN |
| Probe local `KEYBOARD_OPENCLOSE` | NOT RUN | NOT RUN |
| Probe local `INPUTMODE_CONVERSION` | NOT RUN | NOT RUN |

## Elevated-target boundary

Keep FlowIME.Probe running as a normal user. Launch one safe test target elevated and repeat `inspect` / `set`.

| Check | Result |
| --- | --- |
| Window identity still resolves | NOT RUN |
| Executable path is available | NOT RUN |
| IMM status can be read | NOT RUN |
| IMM status can be changed | NOT RUN |
| Failure, if any, is bounded and does not hang Probe | NOT RUN |

FlowIME V1 must not solve an elevated-target failure by making the whole application permanently run as administrator.

## P0 decision

Choose exactly one after the matrix is complete:

- **PASS — IMM backend candidate:** `ImmGetDefaultIMEWnd` + bounded `WM_IME_CONTROL` read/write is deterministic across the core matrix and the visual Chinese/English mapping is confirmed.
- **FAIL — native bridge investigation required:** any core application cannot be read/set reliably, Open/Closed does not correspond to Microsoft Pinyin Chinese/English, or mutation causes focus/input side effects. Stop UI development and investigate the TSF/native bridge path.

**Decision:** PENDING WINDOWS VALIDATION

## Questions that must be answered before Task 8

1. What observable native state distinguishes Microsoft Pinyin Chinese from English in each core application?
2. Is `IMC_GETOPENSTATUS` sufficient, or is conversion mode also required?
3. Does `IMC_SETOPENSTATUS` mutate the intended state without simulated keystrokes?
4. Does behavior differ between Win32, Chromium/Electron, Windows Terminal, and packaged apps?
5. What specifically fails across the normal-user → elevated-target integrity boundary?
6. Is TSF needed only for profile identification, or also for mutation?
7. Is a thin C++/WinRT bridge required for Task 8?

## Observed result: IMC_GET/SETOPENSTATUS rejected for Microsoft Pinyin mode control

On the Windows 11 test host, Microsoft Pinyin produced direct English text both before and after `IMC_SETOPENSTATUS` was changed from Closed to Open. The legacy OpenStatus flag itself changed and read back successfully, but the effective Microsoft Pinyin Chinese/English behavior did not change.

Conclusion: `IMC_GETOPENSTATUS` / `IMC_SETOPENSTATUS` must not be used as FlowIME V1's Microsoft Pinyin Chinese/English backend. The next P0 experiment is `IMC_GETCONVERSIONMODE` / `IMC_SETCONVERSIONMODE`, specifically the `IME_CMODE_NATIVE` bit.
