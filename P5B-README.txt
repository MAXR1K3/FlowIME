FlowIME P5B-1 - WeChat Input Method discovery

This package does NOT yet add a WeChatImeProvider to production automation.
It adds a read-only capture/compare probe so WeChat's real TSF/IMM behavior can
be measured before any provider-specific mutation code is written.

After p0-check.ps1 passes, run:

    .\scripts\p5b-wechat-probe.ps1

Follow the two prompts using the same target text field. Send the final
comparison output back to ChatGPT. Capture files are stored under:

    %LOCALAPPDATA%\FlowIME\probe\

No WeChat IME write operation is performed in P5B-1.
