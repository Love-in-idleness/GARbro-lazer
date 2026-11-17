# Skills useful for GARbro development

These candidates were inspected on 2026-11-17. They are recommendations, not
installed or automatically enabled skills. Repository instructions, the shared
UI, .NET 11, SchemeTool compatibility, small dependencies, lean validation and the
macOS build-only policy take precedence over generic advice.

| Candidate | Source | Useful work |
|---|---|---|
| Avalonia | [linuxdevel/Avalonia-skills](https://github.com/linuxdevel/Avalonia-skills/tree/main/skills/avalonia) | Version-aware Avalonia 12.1.4 UI, styling, performance, input and Android guidance; load only the relevant subsection. Community-maintained. |
| .NET Performance Engineering | [wieslawsoltes/Performance-Skill](https://github.com/wieslawsoltes/Performance-Skill/blob/main/SKILL.md) | Profile browser/preview startup, allocations, native decoding and UI dispatch latency before changing code. Explicitly separates cold/warm paths and managed/native/compositor costs; select a bounded investigation rather than every profiler. |
| GitHub Actions Efficiency | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/github-actions-efficiency) | Eliminate duplicate jobs, tune triggers and cache reuse while retaining required package checks. GitHub-hosted community collection. |
| .NET Test Audit | [TheAngryByrd/dotnet-reliability-skills](https://github.com/TheAngryByrd/dotnet-reliability-skills/blob/main/dotnet-test-audit/SKILL.md) | Review C# and F# tests for actual defect detection and remove proven duplication while retaining compatibility, cancellation and safety contracts. Uses existing frameworks; audit findings do not authorize broad cleanup. |
| Variant Analysis | [trailofbits/skills](https://github.com/trailofbits/skills/tree/main/plugins/variant-analysis) | Find other occurrences of a confirmed bounds, path, selection or serialization bug. Use a narrow search without invoking its parallel-agent workflow by default. |

Prioritize Avalonia, .NET Performance Engineering and Actions efficiency. Keep Ponytail as the default working
style. No sufficiently verified F#-specific candidate was selected in this pass;
our existing F# preference and local codec patterns remain the source of truth.
Broad serialization/concurrency bundles were not selected: some guidance would
replace the established scheme format or introduce unnecessary frameworks.
The inspected Type Design for Performance skill from Aaronontheweb/dotnet-skills
has useful allocation/collection prompts, but its broad type-design rules are
less suitable as a project default than measuring the actual hot path first.

These skills help the coding agent; they are not GARbro runtime dependencies.
If adopted later, pin a reviewed revision and load only the task's relevant
sections. Do not execute installation scripts just to discover their contents.
