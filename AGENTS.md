# Repository Guidelines

## Project Structure & Module Organization

- `Windows/Core/` contains game models and services for Steam discovery, search, ordering, settings, and save directories. Keep reusable logic independent of WinUI.
- `Windows/Launcher/Source/` contains the WinUI application, views, controls, and UI services. Pair XAML files with their `.xaml.cs` code-behind.
- `Windows/Launcher/Data/` holds `KairosoftGames.json`, the application icon, and cover images named by Steam AppID, such as `Assets/Covers/2191490.jpg`.
- `Windows/Test/Program.cs` contains the executable smoke-test suite. `Scripts/BuildRelease.ps1` handles release packaging.
- Generated output belongs in `.Build/`, as configured by `Directory.Build.props`; do not commit it.

## Build, Test, and Development Commands

Run commands from the repository root on Windows. Install the .NET SDK specified by `global.json` (10.0.400, with latest-patch roll-forward). The core targets .NET 8; the launcher targets Windows x64 with WinUI.

- `dotnet build Windows/KairosoftGameToolbox.sln --configuration Debug` restores dependencies and builds the solution.
- `.\RunLauncher.bat` starts the launcher in Debug mode.
- `dotnet run --project Windows/Test/LauncherSmokeTest.csproj` runs headless smoke tests.
- `.\Scripts\BuildRelease.ps1 -Version 0.1.0` builds, tests, publishes, checks application startup, and packages a single executable under `.Build/Package/`. Use `-SkipLaunchCheck` only when desktop startup cannot be checked.

## Coding Style & Naming Conventions

Follow nearby code: four-space C# indentation, file-scoped namespaces, and opening braces on separate lines. Use PascalCase for types and methods and camelCase for parameters and locals. Preserve nullable annotations. Match existing XAML and project-file formatting; no repository-wide formatter or linter configuration is present.

## Testing Guidelines

Tests use a custom `Check(description, condition)` harness, not xUnit or NUnit. Add descriptive checks to `Windows/Test/Program.cs` for changed core behavior. Use temporary synthetic Steam libraries and clean them up; tests should require neither Steam nor network access. No numerical coverage threshold is configured. Update catalog-count and cover checks when adding games. Manually verify affected UI flows for view changes.

## Commit & Pull Request Guidelines

Use `main` as the default branch. Never commit `.TestGames/` game copies or saves, `.Build/` output, or local secrets. Use concise, imperative subjects such as `Fix Steam library path parsing`. Keep changes focused. PRs should describe behavior changes, link relevant issues, report validation commands and results, and include screenshots for visible UI changes.
