FlowIME P5A patch
=================

Base: accepted FlowIME P4 build.
Purpose: introduce the provider architecture without changing production behavior.

Apply:
1. Exit FlowIME from the tray.
2. Extract this patch over C:\FlowIME and allow overwrite.
3. In PowerShell:

   cd C:\FlowIME
   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
   .\scripts\p0-check.ps1

4. If green, start:

   dotnet run --project .\src\FlowIME.App\FlowIME.App.csproj

Expected behavior:
- Existing rules.json requires no migration.
- Microsoft Pinyin remains the only enabled provider.
- Chinese/English behavior must be indistinguishable from P4.
- Diagnostics gains provider metadata only.

Architecture added:
- IInputMethodProvider
- InputMethodProviderRegistry
- ProviderInputMethodBackend
- MicrosoftPinyinProvider

MicrosoftPinyinBackend remains as a backward-compatible facade.
See docs\p5\P5A-PROVIDER-ARCHITECTURE-REPORT.md.
