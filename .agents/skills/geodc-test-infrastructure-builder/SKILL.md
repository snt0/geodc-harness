---
name: geodc-test-infrastructure-builder
description: Add or change reusable geoDC NUnit/WPF test infrastructure. Use for the shared STA runner, WPF Application and resource setup, reusable themed-window or render harnesses, and cross-suite test-host facilities. Do not use merely because a feature or control needs ordinary tests.
---

# geoDC Test Infrastructure Builder

Change the test harness only when existing helpers cannot express the required test safely and repeatedly.

## Workflow

1. Read the target test and production surface, then inspect `geodc/tests/StaTestRunner.cs` and neighboring harness use.
2. Identify the missing facility: STA execution, WPF `Application` ownership, production dictionary loading, render setup, or reusable host lifecycle.
3. Extend the smallest existing helper at the narrowest shared scope; avoid page-specific abstractions in global infrastructure.
4. Prove the helper with a focused consumer test and run the affected suite.

## Rules

- Use the shared STA runner for WPF dependency objects, controls, routed events, templates, and rendering.
- Prefer a real WPF `Application` and production compiled-resource URI when resource or template behavior is under test.
- Make `Application`, dispatcher, window, and disposable ownership explicit; clean up in the same test boundary that creates them.
- Keep assertions on public behavior. Avoid visual-tree trivia, internal template names, and screenshot diffing unless they are the actual contract.
- Use `RenderTargetBitmap` for render-path safety, not as a proxy for subjective visual correctness.
- Do not duplicate package references, NUnit globals, or `StaTestRunner.cs` setup already injected by `Directory.Build.props`.

Feature tests remain owned by the relevant feature skill; this skill owns reusable testing machinery.
