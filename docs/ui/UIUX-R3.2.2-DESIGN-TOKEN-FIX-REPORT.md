# FlowIME UI/UX R3.2.2 — Design Token Contract Fix

## Scope

R3.2.2 is a UI-contract correction on top of R3.2.1. It does not change runtime behavior, P8C.2 functionality, navigation, input switching, gameplay logic, or visual dimensions.

## Fix

`HomePage.xaml` used a literal `MaxWidth="1120"` after the R3.2.1 home polish. R3.1 introduced the shared design token `FlowPageContentMaxWidth`, and the UI contract intentionally requires content pages to consume that token instead of duplicating the numeric value.

The Home page now uses:

```xaml
MaxWidth="{StaticResource FlowPageContentMaxWidth}"
```

The resolved width remains 1120 epx with the current theme resources, so this is visually equivalent while restoring centralized design-system control.

## Validation

- Home page consumes `FlowPageContentMaxWidth`.
- Existing UI contract `UiR3_caps_desktop_content_width_instead_of_stretching_information_across_ultrawide_windows` is satisfied by construction.
- No Core / Windows / Infrastructure / Probe / App Services files changed.
