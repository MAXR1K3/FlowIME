FlowIME P5B-1 read-only WeChat Input Method discovery patch

Base: the P5A version that already passed real-machine validation.

What this patch changes:
- adds read-only `capture` and `compare-captures` commands to FlowIME.Probe;
- adds an automated `scripts\p5b-wechat-probe.ps1` discovery matrix;
- adds Probe comparison tests and documentation.

What this patch deliberately does NOT change:
- FlowIME.App production automation;
- Microsoft Pinyin provider behavior;
- P1/P2/P3/P4 runtime paths;
- rules.json schema;
- any WeChat Input Method state via write operations.

Validation order:
1. Exit FlowIME from the tray.
2. Extract this patch over C:\FlowIME.
3. Run:

   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
   .\scripts\p0-check.ps1

4. If the build/tests are green, run:

   .\scripts\p5b-wechat-probe.ps1

5. Follow the Chinese and English prompts using the same Notepad text field.
6. Send the final comparison output back to ChatGPT.

The capture JSON files are saved under:
%LOCALAPPDATA%\FlowIME\probe\
