---
name: connectivity-help
description: Explain ActionFit Connectivity, its installed skills, state and probe contracts, retry and monitoring behavior, integration boundaries, tests, and safety rules. Use when a user asks how the connectivity package works or which package skill applies.
---

# ActionFit Connectivity Help

Answer in the user's language. Explain the package without running an audit, network probe, Unity validation, or state-changing command unless the user separately requests that operation.

1. Read `PACKAGE_SKILLS.md` first. Treat its generated package identity, complete related-skill table, `$skill-name` invocations, descriptions, and access boundaries as authoritative.
2. Read `Packages/com.actionfit.connectivity/README.md` and `Packages/com.actionfit.connectivity/AI_GUIDE.md` when present. If downloaded, resolve `Library/PackageCache/com.actionfit.connectivity@*` without editing it.
3. Explain `Unknown`, `Checking`, `Online`, and `Offline`; OS reachability versus a real probe; one-shot versus retrying checks; cancellation restoration; recovery waiting; and monitoring Pause/Resume ownership.
4. Keep endpoint choice, UI, SDK gates, lifecycle forwarding, persistence, and product retry policy in the consuming project. For Cat Merge Cafe, identify `InternetCheck` as the compatibility facade without moving its responsibilities into the package.
5. List `README` under `Tools > Package > ActionFit Connectivity` and identify `com.actionfit.connectivity.Editor.Tests` as the deterministic EditMode suite.
6. State that package help and audit must not start monitoring, contact a probe endpoint, expose response bodies or identifiers, change project data, publish, tag, or update the package catalog.
