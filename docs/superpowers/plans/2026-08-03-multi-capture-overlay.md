# Multi-capture Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add F7 screenshot queuing, F8 multi-image analysis, and a stable Enter-to-send chat overlay.

**Architecture:** `AdvisorOrchestrator` owns bounded temporary captures and passes ordered paths through `ICodexCliClient`. The WPF app registers independent F7 and F8 hotkeys, while the overlay keeps only its content region scrollable.

**Tech Stack:** .NET 8, WPF, xUnit, FluentAssertions, Codex CLI

## Global Constraints

- Maximum queued captures: 6.
- F8 includes the current screen after queued screens.
- Clear queued files only after successful analysis.
- Enter sends and Shift+Enter inserts a newline.

---

### Task 1: Multi-image Codex invocation

**Files:**
- Modify: `src/Civ6Companion.App/Advisor/ICodexCliClient.cs`
- Modify: `src/Civ6Companion.App/Advisor/CodexCliClient.cs`
- Test: `tests/Civ6Companion.Tests/Advisor/CodexCliClientTests.cs`

- [ ] Write a failing test asserting one repeated `--image` pair per ordered path.
- [ ] Run the targeted test and confirm it fails because the client accepts one image.
- [ ] Change `AnalyzeAsync` to accept `IReadOnlyList<string>` and append every path.
- [ ] Run the targeted test and confirm it passes.

### Task 2: Bounded capture queue

**Files:**
- Modify: `src/Civ6Companion.App/Advisor/IAdvisorOrchestrator.cs`
- Modify: `src/Civ6Companion.App/Advisor/AdvisorOrchestrator.cs`
- Test: `tests/Civ6Companion.Tests/Advisor/AdvisorOrchestratorTests.cs`

- [ ] Write failing tests for chronological ordering, six-item eviction, success clearing, and failure preservation.
- [ ] Run the targeted tests and confirm the missing queue API failures.
- [ ] Add `QueueCurrentScreenAsync`, bounded capture ownership, multi-image F8 analysis, and disposal.
- [ ] Run the targeted tests and confirm they pass.

### Task 3: F7 application wiring

**Files:**
- Modify: `src/Civ6Companion.App/App.xaml.cs`
- Modify: `src/Civ6Companion.App/Shell/OverlayViewModel.cs`
- Test: `tests/Civ6Companion.Tests/Shell/OverlayViewModelTests.cs`

- [ ] Write a failing test for the queue command and queued status.
- [ ] Run it and confirm failure because the command is absent.
- [ ] Register a separate F7 hotkey and invoke the new queue command.
- [ ] Run the targeted test and confirm it passes.

### Task 4: Stable overlay and keyboard behavior

**Files:**
- Modify: `src/Civ6Companion.App/Shell/OverlayWindow.xaml`
- Modify: `src/Civ6Companion.App/Shell/OverlayWindow.xaml.cs`
- Test: `tests/Civ6Companion.Tests/Shell/InteractionRegressionTests.cs`

- [ ] Write failing layout and keyboard contract tests.
- [ ] Confirm they fail on resizable window, growing composer, and KeyDown handling.
- [ ] Fix window dimensions, use a fixed composer row and Send button, and handle PreviewKeyDown.
- [ ] Run targeted tests and confirm they pass.

### Task 5: Full verification and launch

**Files:**
- Build output: `multicapture/Civ6Companion.App.exe`

- [ ] Run `dotnet test Civ6CodexCompanion.sln --no-restore` and confirm zero failures.
- [ ] Build the app to `multicapture` and confirm zero errors.
- [ ] Restart only the companion process with the new executable.
