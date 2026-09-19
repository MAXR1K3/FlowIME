# FlowIME UI/UX R2 Report

Baseline: P8C.2 GameTextEntry Detection.

## Scope freeze

P8C feature development is intentionally frozen for this branch. The following runtime layers are unchanged:

- FlowIME.Core
- FlowIME.Windows
- FlowIME.Infrastructure
- FlowIME.Probe
- FlowIME.App/Services

UI/UX R2 changes only shell/navigation, page presentation, UI-facing view model projection, and configuration dialogs.

## Information architecture

Primary navigation is reduced to three user tasks:

1. 首页 — live state and quick actions
2. 规则 — application and global-default rules
3. 游戏 — gameplay protection and game text-entry configuration

The former standalone “输入法支持” entry is removed from primary navigation because provider availability is already expressed where the user selects an input method. “关于” moves to the footer and Windows-style Settings remains in the footer.

The 游戏 and 设置 destinations share the existing SettingsPage runtime controls, but render separate modes. This avoids duplicated settings state or duplicated event handlers.

## Home redesign

The home page is now a task-oriented overview:

- live application hero card
- friendly context label: 普通应用 / 全屏应用 / 游戏模式 / 游戏 · 文字输入
- current input state
- direct “设置当前应用” action
- direct “管理全部规则” action with synchronized shell navigation
- compact rule source and target summary

No diagnostics-only details are exposed on the home page.

## Game interaction redesign

Gameplay settings are promoted to a dedicated navigation destination.

- US keyboard baseline is the primary protection switch.
- Hotkey Guard remains a master switch.
- individual Win+Space / Ctrl+Space / legacy combinations are moved under a collapsed advanced Expander.
- GameTextEntry configuration remains tied to the most recently detected game.

## GameTextEntry dialog

The dynamically constructed ContentDialog is replaced with a dedicated responsive XAML dialog.

The dialog provides:

- game identity summary
- enable/disable switch
- standard text-control detection
- hotkey-profile detection
- conditional hotkey fields
- input target selection
- inline validation InfoBar
- explicit profile deletion

The dialog changes presentation only. The existing profile schema, parser, repository and runtime state are unchanged.

## Theme

The existing warm-beige light theme is retained and refined with:

- warm hero surface
- softer accent surface
- larger 14–16 px card radii
- consistent pill treatment

Dark mode continues to use Windows semantic dark resources rather than a custom beige-dark palette.

## Responsive behavior

All changed pages retain the existing effective-pixel breakpoints and compact navigation safe area. Content continues to reflow rather than shrink text or use character ellipsis.
