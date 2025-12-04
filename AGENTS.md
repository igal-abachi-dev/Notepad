# Repository Guidelines

## Project Structure & Module Organization
- Root: `Program.cs`, `App.axaml`, `App.axaml.cs`, and `Notepad.csproj` targeting .NET 9.
- UI markup lives in `Views/` (e.g., `MainWindow.axaml` plus dialogs like `FindReplaceDialog.axaml`, `GoToLineDialog.axaml`, `FontDialog.axaml`, `PageSetupDialog.axaml`, `AboutDialog.axaml`) with code-behind; UI state sits in `ViewModels/` (`MainWindowViewModel`, `FindReplaceViewModel`, `GoToLineViewModel`, `PageSetupViewModel`).
- Domain models reside in `Models/` (`DocumentModel`, `EditorSettings`, `PageSetupSettings`, `SearchSettings`); supporting services are in `Services/` (`FileService`, `SearchService`, `SettingsService`, `PrintService`); value converters live in `Converters/`; assets stay under `Assets/`. Tests live in `Tests/` (xUnit, `Notepad.Tests.csproj`).

## Build, Test, and Development Commands
- `dotnet restore` - install NuGet dependencies.
- `dotnet build` - compile and fail fast on errors.
- `dotnet run` - launch the Avalonia Notepad app locally.
- `dotnet test` - run xUnit suites under `Tests/`.
- `dotnet publish -c Release -r win-x64 --self-contained` - produce a Windows release build; swap the RID for Linux/macOS.
- Optional: `dotnet watch run` for live recompilation during UI work.

## Coding Style & Naming Conventions
- 4-space indentation, trailing newline; braces on new lines for blocks.
- PascalCase for public types/methods/properties; camelCase for locals/parameters; prefix interfaces with `I` (e.g., `IFileService`).
- Follow Avalonia patterns: `.axaml` files for markup, bind via `DataContext`, keep UI-specific logic in code-behind and shared logic in view models/services.
- Prefer `async`/`await` for file dialogs and I/O; avoid blocking the UI thread.

## Testing Guidelines
- Tests live under `Tests/` using xUnit; name files after the unit under test (e.g., `FileServiceTests.cs`, `SearchServiceTests.cs`).
- Use method names `Method_Scenario_ExpectedResult` where helpful; cover happy-path and edge cases for services/viewmodels (file encodings, search/replace, print pagination, recent files, settings behavior).
- Run suites with `dotnet test`; add coverage for new features/services as they land.

## Commit & Pull Request Guidelines
- Use clear, present-tense commit subjects (50-72 chars); Conventional Commit prefixes (`feat:`, `fix:`, `docs:`) encouraged.
- Scope PRs narrowly; include a short description, testing notes (`dotnet build`, `dotnet run`, `dotnet test`), and link any tracking issue.
- For UI changes, attach before/after screenshots or a short clip; note platform-specific behavior or publishing tweaks.

## Security & Configuration Tips
- Do not commit user documents or secrets; place samples in ignored paths outside the repo.
- Keep encoding and line-ending handling deterministic; log only non-sensitive paths when troubleshooting.
