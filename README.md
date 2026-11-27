# Notepad
Notepad (classic) written in C# + Avalonia UI + Native AOT 


in windows 11 , notepad changed from being light single exe (c++ win32 based on windows messages and gdi and com dialogs),
into c++ winrt(uwp/winui xaml) with richtext markdown , tabs , restart manager , cowriter ai.... and more bloat
but they removed the simplicity it had , and made it slower load and heavy

so this is remaking the classic notepad on modern stack so its notepad xaml ui , more maintainable,
it is based on reversing original notepad c code from win10 exe , and porting it to c# and modernizing it

text engine is based of piece Red-Black Tree of stringbuilder text buffer like vs code / vs 2026 editor
instead of ropes<T> of avalonia edit 

and uses textLayout for rendering text



NeoEdit is a plain-text editor based on the Windows 10 Notepad workflow. It opens to a blank document ready for typing and keeps one document per window. It is written in C# and Avalonia, with a self-contained Windows x64 Native AOT release.


<img width="3834" height="2048" alt="image" src="https://github.com/user-attachments/assets/e81298dd-0945-40ee-9a99-a20679ab1a4f" />



In Windows 11, the original Notepad evolved from a lightweight Win32 application into a heavier UWP/WinUI app. While it added features like tabs and AI cowriters, it lost the instant startup and raw simplicity that defined the original.

This project aims to restore that simplicity while using modern engineering:
*   **Zero Bloat:** No AI, no webviews, no heavy frameworks.
*   **Instant Load:** Compiled with **Native AOT** for sub-second startup.
*   **Maintainable:** Written in C# and XAML (Avalonia UI).

## ⚙️ Architecture: NeoEditor Engine
Unlike most .NET editors that rely on `AvaloniaEdit` (which uses a Rope data structure) or standard `TextBox` controls, this project implements a custom text engine from scratch.

### The Piece Table
The core backend (`PieceTreeEngine`) is a direct port of the **VS Code / Visual Studio 2026** text buffer architecture.
*   **Structure:** Uses a Red-Black Tree to manage "Pieces" (spans of text).
*   **Memory:** Utilizes an Append-Only buffer (`StringBuilder`) logic to avoid costly string concatenations.
*   **Performance:** Insertions and Deletions are **O(log N)**. Opening a 10MB file is instant because it doesn't copy text—it just points to it.
https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation

### The Renderer
*   **Virtualization:** Only renders lines currently visible in the viewport.
*   **Direct Drawing:** Bypasses standard controls to draw text directly using `TextLayout` and the underlying OS shaping engine (HarfBuzz/DirectWrite).
*   **Bidi Support:** Auto-detects RTL (Hebrew/Arabic) per line.

## ✨ Features
*   **Native AOT Compilation:** Single executable, minimal dependencies, fast startup.
*   **Polyglot Syntax Highlighting:** Fast, regex-based highlighting (Atom One Light theme) for C#, JSON, XML, HTML, Python, and more.
*   **Modern Editing:**
    *   Multi-level Undo/Redo.
    *   Regex Find/Replace.
    *   Zooming (Ctrl + MouseWheel).
    *   Line Number Gutter.
*   **Visuals:** Pixel-perfect caret rendering and "VS Code-like" selection handling.

## Supported features

- **Files and windows.** New, Open, Save, Save As, drag-and-drop, and recent files. New Window starts a separate process. Closing a modified document offers **Save**, **Don’t Save**, and **Cancel**.
- **Editing.** Single- and multiline selection, Cut, Copy, Paste, Delete, Select All, and F5 to insert the local time and date. Opening a file that starts with `.LOG` appends a timestamp.
- **Find and navigate.** Modeless Find and Replace with selection seeding, match case, direction, and wrap-around. F3 and Shift+F3 repeat the search. Ctrl+G opens Go To Line when Word Wrap is off.
- **Display.** Installed fonts, Word Wrap, zoom, and a status bar showing line, column, encoding, and line endings. The editor supports right-to-left reading order and can show or insert common Unicode control characters.
- **Encodings.** Windows ANSI, UTF-8 with or without BOM, and UTF-16 LE or BE. Open and Save As include encoding selection. **Reopen with Encoding** lets you override detection, and **Default Encoding** sets the format for new documents.
- **Printing.** Page Setup includes paper, orientation, margins, and a preview. Windows printing uses the driver’s print dialog and supports classic header and footer fields such as `&f` for the filename and `&p` for the page number.

From the command line, pass a path to open it. Use `/A` for ANSI, `/W` for UTF-16 LE, or `/P` to print. Use `--` to end option parsing. A missing file can be created from the startup prompt.

## Improvements over Windows 10 Notepad

| Area | NeoEdit behavior |
| --- | --- |
| Editing | Multi-level Undo and Redo with grouped typing and saved-revision tracking. Insert toggles overtype and shows an **OVR** indicator. Character navigation and deletion account for grapheme clusters. |
| Search | Regex and whole-word search. Replace All works from a document snapshot and can be undone in one step. |
| Line endings | Ordinary saves preserve mixed CRLF, LF, and CR separators. The status bar offers explicit conversion as one undoable edit. |
| File safety | Saves stage and flush encoded text before replacing the destination. Saving back to an open file checks for external changes. Hard-linked files on Windows offer **Update All Links**, **Save As**, and **Cancel**. An in-place update keeps a flushed copy of the original beside the file until the write succeeds. |
| Draft recovery | Periodic checkpoints protect unsaved edits, including untitled documents. **File → Recover Drafts** opens a selected draft. Normal launches still start with a blank document. |
| Display and navigation | System, Light, and Dark themes, a toggleable line-number gutter, extension-based syntax highlighting for C#, JSON, XML, HTML, Python, JavaScript/TypeScript, and SQL, and Ctrl+click for web links. |

## Architecture

| Component | Design |
| --- | --- |
| [Avalonia views](Views/) and [view models](ViewModels/) | AXAML views provide menus, dialogs, and status feedback. View models coordinate commands, document state, save prompts, and recovery. |
| [Piece tree](NeoEdit/Backend/PieceTreeBase.cs) | An augmented red-black tree stores spans of an immutable original string and an append-only edit buffer. Character and line metadata support offset lookup without flattening the document on every edit. The design draws on the piece tree described by the VS Code team. [VS Code text buffer reimplementation](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation) |
| [NeoEditor layout](NeoEdit/NeoEditor.Layout.cs) | The custom control shapes text through Avalonia `TextLayout` and the Skia/HarfBuzz stack. It caches line layouts and draws the visible viewport. It uses the same layout for drawing, caret positions, and selection hit-testing. |
| [Search and file services](Services/) | Search reuses a text snapshot for each document revision. The file service handles strict decoding, encoding, and staged saves. The draft store keeps checksummed checkpoints, and the settings service stores preferences in JSON. |
| [Windows printing](Services/WindowsPrinting.cs) and [session handling](Services/WindowsSessionEnd.cs) | Windows integration handles native printing, IME requests, Jump List registration, and draft checkpoints for session end. The main editor, file picker, and editing dialogs use Avalonia. |

## Build and test

## 🛠️ Build
Requirements: .NET 8.0 SDK or higher.

```bash
# Clone
git clone https://github.com/igal-abachi-dev/Notepad.git



The project targets .NET 9. Building the Windows x64 Native AOT release also requires the Windows C++ toolchain, available through the **Desktop development with C++** workload. [Native AOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#prerequisites)

```powershell
dotnet build Notepad.csproj -p:UsedAvaloniaProducts=
dotnet run --project Notepad.csproj --no-build
dotnet test Tests/Notepad.Tests.csproj -p:UsedAvaloniaProducts=
```

The build and test commands use the same Avalonia build-reporting override as the release script.

On Windows, publish and run the release smoke checks from the repository root.

```powershell
.\build-release.cmd --verify
```

The executable is written to `bin/Release/net9.0/win-x64/publish/Notepad.exe`. Omit `--verify` to publish without launching the checks.

## Verification and limits

- The Windows release smoke checks exercise the published executable, including Open and Save As, encoding selection, line-ending conversion, Go To Line, the hard-link save flow, and PDF output when Microsoft Print to PDF is available. [Release checks](ReleaseSmokeTest.cs)
- Workload tests cover a 32 MiB log and editing and navigating a 20,000-character line. Shaping extremely long wrapped lines can still be expensive. [Workload tests](Tests/EditorWorkloadTests.cs)
- The Avalonia file picker browses filesystem paths. Windows Shell virtual locations and extensions are outside its scope. UI Automation exposes the document value. Narrator text-range navigation is a remaining gap. [File picker](Views/FilePickerDialog.axaml.cs) · [Editor platform input](NeoEdit/NeoEditor.PlatformInput.cs)
