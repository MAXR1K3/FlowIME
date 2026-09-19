# P8B.1.1 Diagnostic History Fix

## Purpose

P8B.1 revealed that copying diagnostics can itself change the foreground window.
A single `currentProcess/currentContextSignals` sample therefore cannot reliably
validate the preceding fullscreen application.

## Change

Diagnostics now include the eight most recent privacy-bounded automation decision
records already stored by `AutomationDecisionJournal`. Each line contains only:

- UTC timestamp
- process name
- context signal kinds
- trigger
- decision source
- reason code
- outcome

It intentionally excludes window titles, executable paths, typed text, URLs, and
clipboard contents.

## Behavior

No automation, fullscreen detection, rule resolution, or input-method behavior is
changed. This is observability only.
