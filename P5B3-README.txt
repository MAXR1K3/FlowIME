FlowIME P5B-3 candidate integration

- Adds WeChatInputMethodProvider to the production provider registry.
- Adds providerId to application rules; legacy rules normalize to microsoft-pinyin.
- Adds input-method selection to add/edit rule dialogs.
- Microsoft Pinyin still owns ConversionMode 0x401/0x0 only.
- WeChat Input Method owns OpenStatus Open/Closed only.
- P5B-2 Notepad mutation passed; P5B-3 still requires integrated profile-activation
  and Electron/Chromium host validation on the real Windows machine.
- rules.json schema is v2; existing v1 rules migrate to microsoft-pinyin.
