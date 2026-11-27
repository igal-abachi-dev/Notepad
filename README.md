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



NeoEdit brings the classic Windows 10 Notepad workflow to C# and Avalonia. It opens to a blank document ready for typing, keeps one document per window, and focuses on local plain-text editing. The Windows x64 release is published as self-contained Native AOT.

<img width="3834" height="2048" alt="Notepad window" src="https://github.com/user-attachments/assets/e81298dd-0945-40ee-9a99-a20679ab1a4f" />


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

- **Files and windows.** New, Open, Save, Save As, drag-and-drop, and Recent Files. New Window starts a separate process. Windows registers opened files in the taskbar Jump List. Closing a modified document offers **Save**, **Don’t Save**, and **Cancel**.
- **Editing.** Standard selection and clipboard shortcuts, single- and multiline editing, and F5 to insert the local time and date. Opening a file that starts with `.LOG` appends a timestamp. Insert toggles overtype and shows an **OVR** indicator.
- **Find and navigate.** Modeless Find and Replace with regex, whole word, match case, and wrap-around. Find can start with the selected text. F3 and Shift+F3 repeat the search. Ctrl+G opens Go To Line when Word Wrap is off. Ctrl+click opens web links.
- **Display.** System, Light, and Dark themes, installed fonts, Word Wrap, Ctrl+mouse wheel zoom, a toggleable line-number gutter, and a status bar with line, column, zoom, encoding, and line endings. The editor supports right-to-left reading order and can show or insert common Unicode control characters.
- **Syntax highlighting.** Basic, extension-based highlighting for C#, JSON, XML/XAML, HTML, Python, JavaScript/TypeScript, and SQL. [Highlighting rules](NeoEdit/SyntaxSystem.cs)
- **Encodings.** Windows ANSI, UTF-8 with or without BOM, and UTF-16 LE or BE. Open and Save As include encoding selection. **Reopen with Encoding** overrides detection, and **Default Encoding** sets the format for new documents. The initial default is UTF-8 without BOM.
- **Printing.** Page Setup includes paper, orientation, margins, and a preview. On Windows, Print opens the native print dialog and supports classic header and footer fields such as `&f` for the filename and `&p` for the page number.
- **Windows accessibility.** UI Automation exposes `TextPattern` and `TextPattern2` for character, word, and line reading, caret and selection, and screen geometry. The editor falls back to its basic Value provider if the native text provider fails to initialize. [Windows automation](WindowsAutomation/) · [Fallback regression](Tests/NeoEditorAutomationTests.cs)

## Command-line usage

Run `Notepad.exe` by itself for a blank window or pass a file path to open it. Quote paths with spaces. The first file path is used. Switches are case-insensitive and can appear before or after the path. [Startup parser](App.axaml.cs) · [Command-line tests](Tests/StartupTests.cs)

| Switch | Behavior |
| --- | --- |
| `/A` | Open using the Windows ANSI code page. |
| `/W` | Open as UTF-16 little-endian. |
| `/P` | Print directly using the printer saved in Page Setup or the Windows default printer. After successful submission, Notepad requests window close. |
| `--` | End switch parsing so a switch-shaped argument can be used as a path. |

```powershell
.\Notepad.exe "C:\Notes\meeting notes.txt"
.\Notepad.exe /A "C:\Notes\legacy.txt"
.\Notepad.exe "C:\Notes\unicode.txt" /W
.\Notepad.exe /P "C:\Notes\agenda.txt"
.\Notepad.exe -- ".\draft notes.txt"
```

The last encoding switch wins if both `/A` and `/W` are supplied. Without either, Notepad detects the encoding. If a path without an extension is missing, Notepad tries it with `.txt`. Accepting the create prompt creates an empty file immediately. [Startup file workflow](ViewModels/MainWindowViewModel.Files.cs)

The app also uses `--recover-draft <32-hex-digit ID>` to open a draft in another process and `--verify-release <report-path>` to run the release smoke checks. For everyday use, choose **File → Recover Drafts**. [Startup handling](App.axaml.cs) · [Release script](build-release.cmd)

## Improvements over Windows 10 Notepad

| Area | NeoEdit behavior |
| --- | --- |
| Undo and Unicode editing | Multi-level undo and redo group continuous typing and track the saved revision. Undo history is bounded by entry count and stored text. Arrow keys and deletion respect grapheme clusters such as combining characters, flags, and joined emoji. |
| Search and replacement | Regex and whole-word options extend Find and Replace. Replace All matches against a document snapshot and applies the result as one undoable edit. |
| Line endings | Saves preserve the CRLF, LF, and CR separators in the buffer. The status bar offers explicit conversion as one undoable edit. The Format menu controls line-ending handling on open and paste. |
| File safety | Saves stage and flush encoded text before committing. Saving back to an open file checks for external changes. Save As protects a new destination if another process creates it first. On Windows, hard-linked files offer **Update All Links**, **Save As**, and **Cancel**. An in-place update keeps a flushed recovery copy until the write succeeds. |
| Draft recovery | Periodic, checksummed checkpoints protect unsaved edits, including untitled documents. **File → Recover Drafts** opens a selected draft. Windows session end also attempts to checkpoint pending edits. Ordinary launches still open a blank editor. |
| Display and navigation | System, Light, and Dark themes, a toggleable line-number gutter, extension-based syntax highlighting for C#, JSON, XML, HTML, Python, JavaScript/TypeScript, and SQL, and Ctrl+click for web links. |

## Architecture

| Component | Design |
| --- | --- |
| Application | Avalonia AXAML views provide menus and dialogs. View models coordinate commands and document state. Models hold editor and document settings, and services handle files, search, printing, and recovery. [Views](Views/) · [View models](ViewModels/) · [Models](Models/) · [Services](Services/) |
| Piece tree | `PieceTreeEngine` indexes pieces in an augmented red-black tree. Pieces reference an immutable original string or an append-only `StringBuilder` edit buffer. Per-buffer line starts and subtree counts support offset and line lookup. Edits update the affected pieces and keep CRLF joined across piece boundaries. [Buffer implementation](NeoEdit/Backend/PieceTreeBase.cs) · [VS Code/Monaco piece-tree design](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation) |
| Layout and input | NeoEditor uses Avalonia `TextLayout`, caches line layouts, and draws the visible viewport. Rendering, hit testing, and visual caret movement share the shaped layout. The editor determines base direction per line, supports mixed-direction runs, and keeps IME preedit separate from the document until commit. [Layout](NeoEdit/NeoEditor.Layout.cs) · [Input](NeoEdit/NeoEditor.PlatformInput.cs) |
| Document services | Search reuses a text snapshot for each document revision. Files use explicit encoding and line-ending policies. Drafts keep a checksummed current and previous checkpoint. Settings are stored in per-user JSON. [Search](Services/SearchService.cs) · [Files](Services/FileService.cs) · [Drafts](Services/DraftStore.cs) · [Settings](Services/SettingsService.cs) |

| Component | Design |
| --- | --- |
| [Avalonia views](Views/) and [view models](ViewModels/) | AXAML views provide menus, dialogs, and status feedback. View models coordinate commands, document state, save prompts, and recovery. |
| [Piece tree](NeoEdit/Backend/PieceTreeBase.cs) | An augmented red-black tree stores spans of an immutable original string and an append-only edit buffer. Character and line metadata support offset lookup without flattening the document on every edit. The design draws on the piece tree described by the VS Code team. [VS Code text buffer reimplementation](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation) |
| [NeoEditor layout](NeoEdit/NeoEditor.Layout.cs) | The custom control shapes text through Avalonia `TextLayout` and the Skia/HarfBuzz stack. It caches line layouts and draws the visible viewport. It uses the same layout for drawing, caret positions, and selection hit-testing. |
| [Search and file services](Services/) | Search reuses a text snapshot for each document revision. The file service handles strict decoding, encoding, and staged saves. The draft store keeps checksummed checkpoints, and the settings service stores preferences in JSON. |
| [Windows integration](Services/) | Native Windows APIs handle printing, Jump List registration, and draft checkpoints for session end. The editor handles native IME requests through its Avalonia input client. |
| [Windows accessibility](WindowsAutomation/) | A UI Automation provider exposes the editor through `TextPattern` and `TextPattern2`, including character, word, and line ranges, caret and selection, and screen geometry. It reads the same piece tree and layout as the editor. |

### Editor references

Windows 10 Notepad and Win32 EDIT guide compatibility for selection, clipboard, line endings, and file workflows. The compatibility review included a reconstruction of the `comctl32.dll` EDIT buffer. Notepad++ and Scintilla informed search and recovery checks. Notepad4, Notepads, and Legacy Notepad provide references for lightweight editing workflows. [Win32 EDIT](https://learn.microsoft.com/en-us/windows/win32/controls/about-edit-controls) · [Scintilla search](https://www.scintilla.org/ScintillaDoc.html#Searching) · [Notepad++ backup](https://github.com/notepad-plus-plus/npp-usermanual/blob/master/content/docs/session.md) · [Notepad4](https://github.com/zufuliu/notepad4) · [Notepads](https://github.com/0x7c13/Notepads) · [Legacy Notepad](https://github.com/ForLoopCodes/legacy-notepad)

Zed, Helix, Neovim, Kate/KTextEditor, and AvaloniaEdit provide additional references for buffer design, text boundaries, undo, and ranges. NeoEdit's automated buffer checks compare edits against a plain-string model and verify red-black tree invariants. [Zed](https://zed.dev/blog/zed-decoded-rope-sumtree) · [Helix](https://github.com/helix-editor/helix/blob/master/helix-core/src/graphemes.rs) · [Neovim](https://neovim.io/doc/user/options.html#undolevels) · [KTextEditor](https://api.kde.org/ktexteditor-document.html) · [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) · [Piece-tree tests](Tests/PieceTreeEngineTests.cs)

## Build and test

Use a .NET SDK that can build `net9.0` projects. Windows x64 Native AOT publishing also requires the Visual Studio C++ toolchain with the **Desktop development with C++** workload. [Native AOT prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#prerequisites)

```powershell
git clone https://github.com/igal-abachi-dev/Notepad.git
cd Notepad
dotnet build Notepad.csproj -p:UsedAvaloniaProducts=
dotnet run --project Notepad.csproj --no-build
dotnet test Tests/Notepad.Tests.csproj -p:UsedAvaloniaProducts=
```

From the repository root, publish and run the release checks on Windows.

```powershell
.\build-release.cmd --verify
```

The Windows x64 build writes `Notepad.exe` and its companion native libraries to `bin/Release/net9.0/win-x64/publish/`. Keep the native DLLs with the executable when copying the app. Run `build-release.cmd` to publish without running the checks. [Release script](build-release.cmd)

## Verification and limits

- **Automated tests** cover randomized piece-tree edits and line mapping, selection and clipboard, grapheme and mixed-direction navigation, IME composition, search and replace, file I/O and save conflicts, draft recovery, view models, and dialogs. [Test suite](Tests/)
- **Workload tests** cover a 32 MiB log and a 20,000-character line, with editing, search, Replace All, undo, and save checks. Very long wrapped lines can still be expensive to shape. Opening reads and decodes the whole file, and search can create a full document snapshot. [Workload tests](Tests/EditorWorkloadTests.cs)
- **Published release checks** exercise Open and Save As with encoding selection, line-ending conversion, Go To Line, error dialogs, hard-link saves, simulated Windows session end, native IME requests, and PDF printing when Microsoft Print to PDF is available. A Windows UI Automation client verifies text ranges, caret, selection, geometry, and mouse pass-through. Interactive Narrator and IME candidate-window behavior require separate live checks. [Release checks](ReleaseSmokeTest.cs) · [Windows checks](ReleaseSmokeTest.Windows.cs) · [UI Automation probe](WindowsAutomation/AutomationProbe.cs)
- **File picker scope.** The Avalonia picker browses filesystem paths. Windows Shell virtual locations and shell extensions are outside its scope. [File picker](Views/FilePickerDialog.axaml.cs)

## License

Released under the BSD 3-Clause license. [License text](LICENSE)
