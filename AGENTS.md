# Repository Guidelines

## Project Structure & Module Organization
- Root: `Program.cs`, `App.axaml`, `App.axaml.cs`, and `AvaloniaNotePad.csproj` targeting .NET 8.
- UI markup lives in `Views/` (e.g., `MainWindow.axaml`) with code-behind logic; UI state sits in `ViewModels/` (`MainWindowViewModel`, `TabViewModel`).
- Domain models reside in `Models/`; supporting services are in `Services/` (`FileService`, `FindReplaceService`, `PrintService`); shared commands/helpers belong in `Commands/`; assets stay under `Assets/`.

## Build, Test, and Development Commands
- `dotnet restore` — install NuGet dependencies.
- `dotnet build` — compile and fail fast on errors.
- `dotnet run` — launch the Avalonia Notepad app locally.
- `dotnet publish -c Release -r win-x64 --self-contained` — produce a Windows release build; swap the RID for Linux/macOS.
- Optional: `dotnet watch run` for live recompilation during UI work.

## Coding Style & Naming Conventions
- 4-space indentation, trailing newline; braces on new lines for blocks.
- PascalCase for public types/methods/properties; camelCase for locals/parameters; prefix interfaces with `I` (e.g., `IFileService`).
- Follow Avalonia patterns: `.axaml` files for markup, bind via `DataContext`, keep UI-specific logic in code-behind and shared logic in view models/services.
- Prefer `async`/`await` for file dialogs and I/O; avoid blocking the UI thread.

## Testing Guidelines
- No dedicated test project yet; add xUnit/NUnit tests under `Tests/` focusing on `Services/` and `ViewModels/` behavior.
- Name files after the unit under test (e.g., `FileServiceTests.cs`) and methods as `Method_Scenario_ExpectedResult`.
- Run suites with `dotnet test`; include happy-path and edge cases for new features.

## Commit & Pull Request Guidelines
- Use clear, present-tense commit subjects (50–72 chars); Conventional Commit prefixes (`feat:`, `fix:`, `docs:`) encouraged.
- Scope PRs narrowly; include a short description, testing notes (`dotnet build`, `dotnet run`, `dotnet test`), and link any tracking issue.
- For UI changes, attach before/after screenshots or a short clip; note platform-specific behavior or publishing tweaks.

## Security & Configuration Tips
- Do not commit user documents or secrets; place samples in ignored paths outside the repo.
- Keep encoding and line-ending handling deterministic; log only non-sensitive paths when troubleshooting.