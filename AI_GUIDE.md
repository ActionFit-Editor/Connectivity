# AI Guide - ActionFit Connectivity

This file is shipped inside the UPM package so an AI assistant in a consuming Unity project can understand the package without access to the source project's `Docs/AI` folder.

## Package Identity

- Package ID: `com.actionfit.connectivity`
- Display name: ActionFit Connectivity
- Repository: `https://github.com/ActionFit-Editor/Connectivity.git`
- Current package version at generation time: `1.0.5`
- Unity version: `6000.2`
- Runtime dependency: Unity built-in module `com.unity.modules.unitywebrequest` `1.0.0`

## Purpose

ActionFit Connectivity provides reusable connectivity state, reachability/probe composition, retry, recovery waiting, automatic monitoring, and Pause/Resume behavior without depending on game UI, advertising, analytics, Firebase, or initialization managers.

The package owns connectivity evaluation only. Consuming projects own endpoint selection, configuration persistence, UI presentation, SDK gating, application lifecycle forwarding, and product-specific retry decisions.

## Agent Skills

- `Skills~/manifest.json` registers schema v2 `connectivity-help` and `connectivity-audit` for Codex and Claude with read-only access.
- Help reads the generated `PACKAGE_SKILLS.md` inventory before explaining state, probe, retry, monitoring, tests, and adapter boundaries.
- Audit inspects source and project adapters without starting monitoring, contacting a probe endpoint, invoking Unity, editing files, or changing package and project state. It compares Git status before and after inspection and reports missing evidence instead of mutating the repository.

## Project Router Registration

This package should be listed in `Packages/com.actionfit.custompackagemanager/PACKAGE_AI_GUIDE_ROUTER.md`.

Requested router entry:

- `Packages/com.actionfit.connectivity/AI_GUIDE.md` - ActionFit Connectivity provides reusable reachability/probe state, retry, monitoring, recovery waiting, and project adapter boundaries.

Read this file when:

- changing files under `Packages/com.actionfit.connectivity/`
- integrating `ConnectivityService` into a project
- changing connectivity states, retry timing, automatic monitoring, Pause/Resume, or Unity probes
- changing the Cat Merge Cafe `InternetCheck` compatibility facade
- preparing a release for `com.actionfit.connectivity`

## Runtime Architecture

- `ConnectivityState` is `Unknown`, `Checking`, `Online`, or `Offline`.
- `IConnectivityReachabilityProvider` supplies an OS-level `ConnectivityReachability` hint. Only `NotReachable` short-circuits a probe; `Unknown` still probes.
- `IConnectivityProbe` performs the real remote check. Operational network failures should return `false`; cancellation should propagate.
- `IConnectivityObservationProbe` performs one bounded HTTPS observation and returns connectivity success, response code, parsed UTC `Date`, optional parsed `Age`, and round-trip duration without exposing response bodies or arbitrary raw headers.
- `ConnectivityObservation.HasFreshServerDate` requires successful HTTP 2xx/3xx connectivity, a valid UTC `Date`, a valid absent-or-nonnegative `Age`, and `Age` absent or zero.
- `ConnectivityOptions` validates an absolute HTTP/HTTPS endpoint, positive timeout/check interval values, and a non-negative retry interval.
- `ConnectivityService.CheckNowAsync` runs one attempt.
- `ConnectivityService.CheckWithRetryAsync` runs one immediate attempt plus `MaxRetryCount` retries.
- `WaitForOnlineAsync` only waits for a later Online state. It does not start monitoring or perform hidden network work.
- `StartMonitoring` runs immediately and then waits `CheckInterval`; while paused it skips checks and waits `RetryInterval` before observing state again.
- `ResumeAsync` clears Pause and runs one immediate check.

The service serializes concurrent checks and restores the last stable state if cancellation or an unexpected probe exception interrupts a `Checking` state.

The public async contract deliberately uses the BCL `Task` type so the reusable package does not require a consuming project to install UniTask. Cat Merge Cafe may await those tasks from its UniTask-based compatibility facade.

## Unity Adapters

- `UnityReachabilityProvider` maps `Application.internetReachability`.
- `UnityPingConnectivityProbe` performs the optional ICMP first attempt.
- `UnityWebRequestConnectivityProbe` sends a HEAD request and accepts HTTP 2xx/3xx success. Observation calls can add `Cache-Control: no-cache, no-store, max-age=0` and `Pragma: no-cache` and parse bounded response metadata before disposing the request.
- `FallbackConnectivityProbe` tries probes in order and stops after the first success.

Do not log endpoint response bodies, authentication material, raw user identifiers, or advertising identifiers from connectivity diagnostics.

## Cat Merge Cafe Compatibility Boundary

Cat Merge Cafe keeps `Assets/_Project/_Shared/Util/InternetCheck/InternetCheck.cs` as the project facade. It translates `InternetCheckSO` into `ConnectivityOptions`, composes ICMP then HTTP probes, and maps only stable Online/Offline states to the existing boolean events.

Preserve these compatibility behaviors:

- `InternetCheck.IsConnected` starts with the legacy optimistic `true` value until the first stable result.
- `OnDisconnected`, `OnReconnected`, and `OnStatusChanged` do not fire for Unknown or Checking.
- `OnStartupConnectionWaitBegan` and `OnStartupConnectionWaitEnded` remain project UI events, not package events.
- `InternetCheckSO` serialized field names and Resources path remain unchanged.
- Existing UI, ads, analytics, Firebase, and game initialization callers should not reference the package directly unless a separate migration is approved.

## Testing And Failure Rules

- Run `com.actionfit.connectivity.Editor.Tests` in EditMode. Test Unknown initial state, Checking-to-stable transitions, OS NotReachable short-circuit, reachable/unknown probe use, retry count, later recovery, cancellation restoration, recovery wait, Pause/Resume, and fallback ordering.
- Do not use a real public endpoint in unit tests. Inject fake reachability, probe, and delay implementations.
- Test observation parsing with fixed headers and round-trip durations. A positive or malformed `Age`, missing or malformed `Date`, HTTP failure, or cancellation must not produce a fresh server date.
- A probe cancellation is not Offline. Restore the last stable state and propagate cancellation.
- Do not add hidden retries to `CheckNowAsync`; callers choose the retrying API explicitly.
- Package code must not delete, migrate, or reset project data based on connectivity state.

## Package Tools Menu

- Unity menu root: `Tools/Package/ActionFit Connectivity/`.
- `README`: opens this package README.
- Do not add README access back to Custom Package Manager package rows or Project Files.

## Release Notes

- Publishing is manual through Custom Package Manager.
- Before reusing a version, check remote Git tags. Published tags are immutable.
- If this package is modified after a version is tagged, bump to the next unused patch version before publishing.
