FlowIME P5B-2: WeChat Input Method mutation candidate

This patch does NOT integrate WeChat Input Method into FlowIME.App.
It only adds a guarded mutation probe for the OpenStatus distinction discovered in P5B-1.

Run p0-check first, then:
  .\scripts\p5b-wechat-mutation.ps1

The command refuses to write unless the active TSF profile exactly matches the WeChat profile observed on the user's machine.
