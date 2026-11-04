# Working preferences

- Prefer F# for new pure data transformations, validation rules and task-planning logic when it is a good fit. Keep platform lifecycle and interop code in C# where that is clearer. Do not rewrite stable code merely to change language.
- Maintain complete English, Simplified Chinese, Russian and Japanese resources for application-owned UI text, including dynamic menus, settings, notifications, errors and offline help. Keep protocol field names, format identifiers and original legal notices intact.
- Use one simple outlined icon family and review actual desktop and mobile screenshots when changing the UI. Refine spacing, hierarchy, responsive layouts and empty states as part of UI work.
- Linux audio uses the distribution SDL2 library. Preserve native Wayland and text-input support.
- Preserve the side-loaded Windows preview identity. Microsoft Store uploads use the identity in tools/installers/store-identity.json and include the .NET runtime; other desktop packages require the external runtime.
