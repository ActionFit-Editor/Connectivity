---
name: connectivity-audit
description: Audit ActionFit Connectivity source and project adapters for real-probe semantics, retry counts, cancellation restoration, monitoring lifecycle, and privacy-safe diagnostics without changing files or network state. Use when reviewing connectivity package changes or integrations.
---

# Audit ActionFit Connectivity

Keep the audit read-only. Do not start monitoring, perform a real network probe, invoke Unity, edit files, install dependencies, or publish package state.

1. Read the repository instructions only, so project routing and safety rules apply before inspection.
2. From the repository root, capture `git status --short --untracked-files=all` as the audit baseline. Preserve all pre-existing changes.
3. Resolve the physical package root from `Packages/com.actionfit.connectivity`; otherwise use `Library/PackageCache/com.actionfit.connectivity@*` without editing it. Then read the package `README.md` and `AI_GUIDE.md`, plus the consuming project's connectivity architecture document and adapter when present.
4. Use `rg` and read-only file inspection to trace `ConnectivityState`, `CheckNowAsync`, `CheckWithRetryAsync`, `WaitForOnlineAsync`, `StartMonitoring`, `Pause`, `ResumeAsync`, reachability providers, probes, options, and tests. Do not execute runtime checks.
5. Verify and report evidence for these contracts:
   - Online requires a successful real probe; OS reachability alone is not success.
   - One-shot checks do not hide retries, while retrying checks perform one immediate attempt plus the configured additional count.
   - Cancellation and unexpected probe failures restore the prior stable state and do not publish false Offline.
   - Waiting for Online creates no hidden monitoring or network request.
   - Pause/Resume and monitor ownership do not leak UI, SDK, analytics, Firebase, or persistence concerns into the package.
   - Diagnostics omit response bodies, credentials, advertising identifiers, and raw user identifiers.
6. Inspect package dependencies, asmdefs, and deterministic test coverage. For Cat Merge Cafe, verify the `InternetCheck` facade retains serialized settings and stable-event compatibility without directly changing it.
7. Capture the same Git status command again and compare it with the baseline. If state changed during the audit, report the paths and do not claim a no-change result.
8. Return findings grouped as passed contracts, risks, missing evidence, and recommended validation. Mention EditMode or full-project tests as follow-up commands; do not run them from this skill.
