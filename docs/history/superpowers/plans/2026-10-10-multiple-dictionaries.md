# Multiple Dictionaries Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline. The user approved implementation; continue without additional design approval.

**Goal:** Implement the approved toolbar, focus, ordered dictionary configuration and independent vertically scrolling results.

**Architecture:** MainWindow owns ordered DictionaryResultView sections, each with its own DictionaryService and WebViewController. Composition WebView controls support WPF clipping. Browser layout events report content height, wheel deltas and anchor offsets to the outer ScrollViewer without merging HTML documents.

**Tech Stack:** Existing .NET 8, WPF and WebView2 package; no additional dependency.

**Spec:** ../specs/2026-10-10-multiple-dictionaries.md

## Global Constraints
- Maximum eight paths, 12 deduplicated candidates, 150 ms debounce and 50 history entries.
- Required colors: #ba5b49, #3271ae, #f5f2e9.
- Preserve existing Core, AudioBridge and EntryBridge. Only extend browser layout behavior as needed for independent sections.
- No Git metadata available: work in the authorized directory; no branch, commit or push.

## Review Focus
- Settings cancel/invalid paths: active readers and saved configuration unchanged.
- Failed dictionary: others continue; all misses preserve previous results/history.
- Search completion focus: input remains enabled and focused, no duplicate submission.
- Scroll/anchor/fold: outer viewer follows content size and anchor target in the correct section.
- Closure or path replacement: stale candidate tasks cannot access disposed controls or reopen popup.

## Task 1: Toolbar and Focus
- [x] Add integration assertions for absent SearchButton, enabled/focused input after Enter, forward/back navigation; reproduce baseline focus/button failures.
- [x] Implement toolbar templates, read-only busy input and forward navigation in MainWindow.
- [x] Run WPF regression using real Oxford MDX.

## Task 2: Ordered Configuration and Result Sections
Files: DictionarySettings.cs, SettingsWindow.xaml/.cs, DictionaryResultView.cs, MainWindow.xaml/.cs.
Interfaces: DictionarySettings.Load/Save/ValidatePaths; DictionaryResultView.Service, Browser, Controller, OpenAsync/ShowBrowser/ShowError/Dispose; WebViewController.InitializeAsync/ShowEntryAsync/ShowMessageAsync; MainWindow.ApplyDictionaryPathsAsync(IEnumerable<string>).
- [x] Add settings tests for bounds, invalid/duplicate paths and persistence; add two-dictionary integration assertions.
- [x] Implement settings and owned result views; route shared lookup/history/candidates through ordered sections.
- [x] Verify independent DOM results, missing sections, unified links/history and candidate ordering.

## Task 3: Document Layout and Final Regression
Files: WebViewController.cs, LayoutBridge.js, Mdict.Wpf.csproj, integration tests, README.
Interfaces: WebViewController layout height/wheel/anchor events, active browser disposal; DictionaryResultView relays layout to the parent viewer.
- [x] Verify automatic heights, global wheel/anchor scrolling, folding and image loading.
- [x] Run solution build, Core self-tests, existing JS suite and extended WPF regression.
- [x] Perform independent code review and focused follow-ups, resolve required findings, document native UI/viewport-layout limits and deliver runnable v0.6.0.
