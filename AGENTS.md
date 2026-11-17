# Working preferences

- After completing repository work, commit the finished changes and push the working branch to `origin` automatically, unless the user explicitly asks to keep the work local. Report the pushed branch and commit; publishing a release remains a separate action.
- Prefer F# for new pure data transformations, validation rules and task-planning logic when it is a good fit. Keep platform lifecycle and interop code in C# where that is clearer. Do not rewrite stable code merely to change language.
- Maintain complete English, Simplified Chinese, Russian and Japanese resources for application-owned UI text, including dynamic menus, settings, notifications, errors and offline help. Keep protocol field names, format identifiers and original legal notices intact.
- Use one simple outlined icon family and review actual desktop and mobile screenshots when changing the UI. Refine spacing, hierarchy, responsive layouts and empty states as part of UI work.
- Linux audio uses the distribution SDL2 library. Preserve native Wayland and text-input support.
- Preserve the side-loaded Windows preview identity. Microsoft Store uploads use the identity in tools/installers/store-identity.json and include the .NET runtime; other desktop packages require the external runtime.

- Keep release validation lean: run focused regressions once for changed behavior, then build packages with a short package check. Do not repeat full suites across architectures or re-run successful checks during packaging. macOS builds packages only; do not run macOS tests. The maintainer performs post-release manual review.

- For coding work, use the pinned [Ponytail skill](.agents/skills/ponytail/SKILL.md) in full mode by default. User instructions and the focused-validation/macOS build-only policy above take precedence.

# Installed development skills

The following skills are pinned in [.agents/skills/sources.lock.json](.agents/skills/sources.lock.json). Load the matching skill and only the references needed for the current task:

- [Avalonia](.agents/skills/avalonia/SKILL.md): shared UI, input, rendering and platform packaging. Keep this project's .NET 11 target, Material styles and native Wayland default even where generic examples differ.
- [.NET Performance Engineering](.agents/skills/dotnet-performance/SKILL.md): investigate measured startup, browser, preview, allocation or rendering costs before optimizing.
- [GitHub Actions Efficiency](.agents/skills/github-actions-efficiency/SKILL.md): CI triggers, caches, duplicate work and release workflow changes.
- [.NET Test Audit](.agents/skills/dotnet-test-audit/SKILL.md): C#/F# test value and scoped test cleanup; preserve compatibility and safety contracts.
- [Variant Analysis](.agents/skills/variant-analysis/SKILL.md): search for other occurrences of a confirmed bug. Use its narrow direct workflow by default.

These are agent resources, not runtime dependencies. Their general examples do not authorize new frameworks, broad test runs, external messages or unrelated changes. The user's standing authorization to commit and push finished work continues to apply.
