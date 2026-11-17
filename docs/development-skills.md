# Skills useful for GARbro development

These candidates were inspected on 2026-11-17. They are recommendations, not
installed or automatically enabled skills. Repository instructions, the shared
UI, .NET 11, SchemeTool compatibility, small dependencies, lean validation and the
macOS build-only policy take precedence over generic advice.

| Candidate | Source | Useful work |
|---|---|---|
| Avalonia | [linuxdevel/Avalonia-skills](https://github.com/linuxdevel/Avalonia-skills/tree/main/skills/avalonia) | Version-aware Avalonia 12.1.4 UI, styling, performance, input and Android guidance; load only the relevant subsection. Community-maintained. |
| Type Design for Performance | [Aaronontheweb/dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills/tree/master/skills/csharp-type-design-performance) | Allocation, collection and enumeration review in browser/preview/decoder hot paths. Treat suggestions as review prompts, not blanket rewrites. |
| GitHub Actions Efficiency | [github/awesome-copilot](https://github.com/github/awesome-copilot/tree/main/skills/github-actions-efficiency) | Eliminate duplicate jobs, tune triggers and cache reuse while retaining required package checks. GitHub-hosted community collection. |
| Variant Analysis | [trailofbits/skills](https://github.com/trailofbits/skills/tree/main/plugins/variant-analysis) | Find other occurrences of a confirmed bounds, path, selection or serialization bug. Use a narrow search without invoking its parallel-agent workflow by default. |

Prioritize Avalonia and Actions efficiency. Keep Ponytail as the default working
style. No sufficiently verified F#-specific candidate was selected in this pass;
our existing F# preference and local codec patterns remain the source of truth.
Broad serialization/concurrency bundles were not selected: some guidance would
replace the established scheme format or introduce unnecessary frameworks.
