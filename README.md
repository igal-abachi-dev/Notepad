# Notepad
Notepad (classic) written in C# + Avalonia UI + Native AOT 


in windows 11 , notepad changed from being light single exe (c++ win32 based on windows messages and gdi and comdl32 dialogs and comctl32 edit control with edit buffer: Single contiguous HLOCAL + memmove with single-level undo , and kernel32.dll FindNLSStringEx for search and replace),

into c++ winrt(uwp/winui xaml) with Microsoft 365 (Office) build of RichEdit , richtext markdown , tabs , restart manager , cowriter ai.... and more bloat
but they removed the simplicity it had , and made it slower load and heavy
but it did used: RichEditD2DPT (RichEdit Direct2D Plain Text). Using Direct2D and DirectWrite instead of legacy GDI allows hardware-accelerated scrolling, crisp typography, color emoji rendering

so this is remaking the classic notepad on modern stack so its notepad xaml ui , more maintainable, and improved,
it is based on reversing original notepad c code from win10 exe , and porting it to c# and modernizing it

text engine is based of piece Red-Black Tree of stringbuilder text buffer like vs code / vs 2026 editor
instead of ropes<T> of avalonia edit 

and uses textLayout for rendering text



NeoEdit brings the classic Windows 10 Notepad workflow to C# and Avalonia. It opens to a blank document ready for typing, keeps one document per window, and focuses on local plain-text editing. The Windows x64 release is published as self-contained Native AOT.


Why Classic Notepad Hit Those Limits:
Because it relied directly on comctl32's "Edit" control, all of classic Notepad's well-known quirks came straight from that control's internal buffer design:
• The memmove bottleneck: Typing or deleting a character near the top of a 50 MB file forced comctl32 to LocalReAlloc and memmove tens of megabytes of UTF-16 characters in a single contiguous HLOCAL block on the UI thread, freezing the window.
• Single-level undo: The classic "Edit" control only stores one previous state buffer (EM_UNDO literally swaps the current text and the single saved undo buffer, so pressing Ctrl+Z twice just redoes what you undid).
• Direct buffer access for Find/Replace: Instead of sending WM_GETTEXT (which would allocate a second giant copy of the file), classic Notepad sent EM_GETHANDLE, called LocalLock to get a raw WCHAR* pointer directly into the Edit control's internal memory, and ran FindNLSStringEx straight across it. 


<img width="3834" height="2048" alt="Notepad window" src="https://github.com/user-attachments/assets/e81298dd-0945-40ee-9a99-a20679ab1a4f" />


In Windows 11, the original Notepad evolved from a lightweight Win32 application into a heavier UWP/WinUI app. While it added features like tabs and AI cowriters, it lost the instant startup and raw simplicity that defined the original.

This project aims to restore that simplicity while using modern engineering:
*   **Zero Bloat:** No built-in AI, webviews, or heavy frameworks.
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

- **Files and windows.** New, Open, Save, Save As, drag-and-drop, and Recent Files. **File → Open Containing Folder** opens the current file’s folder in the system file manager. New Window starts a separate process. Windows registers opened files in the taskbar Jump List. Closing a modified document offers **Save**, **Don’t Save**, and **Cancel**.
- **Editing.** Standard selection and clipboard shortcuts, single- and multiline editing, and F5 to insert the local time and date. **Edit → Insert GUID** inserts a new hyphenated GUID with Ctrl+Shift+G (Cmd+Shift+G on macOS). Opening a file that starts with `.LOG` appends a timestamp. Insert toggles overtype and shows an **OVR** indicator.
- **Find and navigate.** Modeless Find and Replace with regex, whole word, match case, and wrap-around. Find can start with the selected text and offers **Find Next** and **Find Previous**, also available with F3 and Shift+F3. Search feedback stays inline. **Highlight matches** is off by default. Enable it to outline the active result and preview other visible matches while Find or Replace is open. Ctrl+G opens Go To Line when Word Wrap is off. Ctrl+click opens web links.
- **Display.** System, Light, and Dark themes, installed fonts, Word Wrap, Ctrl+mouse wheel zoom, a toggleable line-number gutter, and a status bar with line, column, zoom, encoding, and line endings. The editor supports right-to-left reading order and can show or insert common Unicode control characters.
- **Syntax highlighting.** Line-based rules cover common code, scripting, build, config, and markup formats. Structured logs highlight timestamps, severity, IP addresses, and stack frames. Named files use their filename or extension. Untitled text can be recognized as JSON, XML, or HTML from its first 256 characters. Use the status bar to override the language or return to **Automatic**. [Language detection](NeoEdit/SyntaxSystem.cs) · [Additional rules](NeoEdit/SyntaxSystem.Rules.cs)
- **Font ligatures.** Windows keeps Consolas as the default. Choose an installed coding font such as Cascadia Code, Fira Code, or JetBrains Mono in **Format → Font**. The dialog previews `!=`, `=>`, and `->` and offers a ligature toggle. Font shaping leaves the saved text unchanged.
- **Auto-indent.** Enable it in **Format** to carry the current line's leading spaces or tabs into a new line when you press Enter. It is off by default.
- **Comments.** **Edit → Toggle Line Comment** (`Ctrl+/`) and **Toggle Block Comment** (`Ctrl+Shift+/`) use the current language's comment syntax and take one Undo. For languages with a single comment style, either command uses that style.
- **Encodings.** Windows ANSI, UTF-8 with or without BOM, and UTF-16 LE or BE. Open and Save As include encoding selection. **Reopen with Encoding** overrides detection, and **Default Encoding** sets the format for new documents. The initial default is UTF-8 without BOM. If ANSI cannot represent the text, Notepad offers to save as UTF-8.
- **Unusual files.** Embedded NUL characters survive open and save. Use **Edit → Replace NUL Characters with Spaces** for an undoable conversion. Opening files larger than 200 MiB asks for confirmation. The open limit is 1 GiB, exclusive.
- **File picker.** Type a folder path for suggestions, or type a name while the file list has focus to select a matching entry. Click a column header to sort in either direction. Press **F2** to rename the selected file or folder.
- **Administrator save.** On Windows, a permission-denied save to the original file can offer **Save as Administrator**. Windows asks for UAC consent, and the save still checks for changes on disk and existing hard links.
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

## Text tools

- **Edit → Convert / Encode** offers standard Base64, MIME Base64, Base64URL, quoted-printable, URL components, HTTP form values, and HTTP Basic authorization. The codecs use UTF-8 for text. JOSE tools inspect JWT and compact JWE, encode explicitly unsigned JWTs, reassemble compact JWE from its encoded parts, and format JWKs. Inspection is local and displays token data without verifying signatures or decrypting JWE payloads.
- **Edit → Format Text** beautifies or minifies JSON, XML, XHTML, HTML5, CSS, JavaScript, and TypeScript. Use XML for Avalonia `.axaml` and WPF `.xaml` files. HTML5, XHTML, and XAML formatting adjusts whitespace inside tags and preserves text, including spaces between elements. The XML reader accepts XHTML5's bare `<!DOCTYPE html>` and keeps DTD processing disabled. The CSS formatter handles modern CSS, including common CSS Modules and Tailwind syntax. JavaScript and TypeScript retain existing line breaks and reject ambiguous slash syntax. JSX/TSX and SCSS are outside the supported syntax.
- Text tools work on the selection or the full document when nothing is selected. One Undo restores the previous text. Choose an encoding directly under **Tools → View File as Encoding** to reopen the file or under **Convert File Encoding** to set the encoding and BOM for the next save.
- **View → Show All Characters** displays spaces, tabs, line endings, and nonprinting characters as faint markers. [Text tools](Services/Tools/) · [Display implementation](NeoEdit/NeoEditor.Layout.cs)
- **Ctrl+E** searches Google for the selected text. Right-click a selection to look up a term, translate it, or ask Google AI Mode to explain, summarize, or rewrite it. Choosing an action sends the selected text to Google in your default browser. [Browser actions](Services/Tools/SelectionWebLink.cs)
- Hold **Ctrl** (**Command** on macOS) over an HTTP(S) URL to underline it, then click to open it, including destinations in Markdown links.
- **Copy to New Window** opens selected text in an untitled window. A complete recognized Markdown code fence is stripped from the copy and supplies a syntax and Save As hint, such as `snippet.ps1` for PowerShell.
- **Inspect Characters** shows Unicode code points and HTML entities for the caret or selected text. Use **Show All Characters** to find invisible characters visually.
- **File → Run Script** runs PowerShell, CMD, and batch scripts on Windows. Clean scripts run directly. Untitled or edited scripts ask for **Save and Run** before starting the interpreter.
- **Multiline search.** Enable regex to match `\r`, `\n`, or `\r\n` in a pattern. Regex replacements also accept `\r`, `\n`, `\t`, and `\\` alongside capture references such as `$1`.
- **Editing shortcuts.** **Shift+Enter** inserts a blank line below. **Alt/Option+Up/Down** moves lines, and adding **Shift** duplicates them. Start a column selection with **Alt/Option+drag** or **Alt/Option+Shift+Left/Right**, then use **Alt/Option+Shift+Up/Down** to extend the rectangle across rows. Column selection uses unwrapped logical lines. Paste one value across all rows or paste one value per row.
- **Edit → Change Case** converts the selection to uppercase, lowercase, Title Case, or camelCase as one undoable edit.
- **View → Read-only Mode** locks editing while leaving navigation, Find, selection, and Copy available. **File → Reload from Disk** (`Ctrl+R`) checks for unsaved edits before reopening the current path.
- **View → Tail File** reads updates to the open file and follows the end. Tailing is read-only, starts only on a clean document, and checks changed files asynchronously, including after truncation or replacement.
- **DOS art.** `.nfo` and `.diz` files default to OEM 437 unless an encoding is explicit or a Unicode BOM is present. The editor uses a monospace font and disables wrapping for those files by default. OEM 437 is also available in the encoding menus.

## Improvements over Windows 10 Notepad

| Area | NeoEdit behavior |
| --- | --- |
| Undo and Unicode editing | Multi-level undo and redo group continuous typing and track the saved revision. Undo history is bounded by entry count and stored text. Arrow keys and deletion respect grapheme clusters such as combining characters, flags, and joined emoji. |
| Search and replacement | Regex and whole-word options extend Find and Replace. Replace All matches against a document snapshot and applies the result as one undoable edit. |
| Line endings | Saves preserve the CRLF, LF, and CR separators in the buffer. The status bar offers explicit conversion as one undoable edit. The Format menu controls line-ending handling on open and paste. |
| File safety | Saves stage and flush encoded text before committing. Saving back to an open file checks for external changes. Save As protects a new destination if another process creates it first. On Windows and Linux, hard-linked files offer **Update All Links**, **Save As**, and **Cancel**. An in-place update keeps a flushed recovery copy until the write succeeds. |
| Draft recovery | Periodic, checksummed checkpoints protect unsaved edits, including untitled documents. **File → Recover Drafts** opens a selected draft. Windows session end also attempts to checkpoint pending edits. Ordinary launches still open a blank editor. |
| Display and navigation | System, Light, and Dark themes, a toggleable line-number gutter, extension-based syntax highlighting for C#, JSON, XML, HTML, Python, JavaScript/TypeScript, and SQL, and Ctrl+click for web links. |

## How NeoEdit differs from classic Notepad

The Windows 10 reference is a C++ Win32 rewrite. Its message loop dispatches commands to a main window, a system multiline `EDIT` control, and a native status bar. A WinEvent hook refreshes caret and zoom information, and settings live in the registry. Opening loads text into `EDIT`, Find reads it through `EM_GETHANDLE`, Replace uses `EM_REPLACESEL`, and Save reads it through `GetWindowTextW`. The reference uses system dialogs, including COM Common Item Dialogs for files with a legacy common-dialog fallback. CraftEdit and the common-dialog reconstructions are separate reference sources. [Win32 EDIT](https://learn.microsoft.com/en-us/windows/win32/controls/about-edit-controls) · [EM_GETHANDLE](https://learn.microsoft.com/en-us/windows/win32/controls/em-gethandle)

| Area | Classic Win32 reference | NeoEdit |
| --- | --- | --- |
| Text buffer | Edit models the studied Common Controls v6 buffer as one movable UTF-16 `HLOCAL`. It uses `LocalReAlloc` when capacity changes and `memmove` to shift the tail for edits in the middle. | An augmented red-black piece tree locates offsets in logarithmic time in the number of pieces. Local edits update pieces without shifting the document tail. Total edit cost also depends on the changed text and the pieces touched. [Piece tree](NeoEdit/Backend/PieceTreeBase.cs) |
| Undo | System EDIT provides one undoable operation, which can be toggled again with `EM_UNDO`. [Win32 undo](https://learn.microsoft.com/en-us/windows/win32/controls/em-undo) | Multi-level Undo and Redo group continuous typing, track the saved revision, and limit history by entry count and retained text. [Editing tests](Tests/NeoEditorTests.cs) |
| Rendering and input | The native EDIT control owns rendering, selection, IME, and caret movement. The reference recreates EDIT when Word Wrap changes. | NeoEditor caches shaped line layouts and draws the visible viewport. Wrap and zoom preserve the document and undo history. The editor handles IME preedit, visual movement in mixed-direction text, and grapheme-aware character navigation and deletion. [Layout tests](Tests/NeoEditorTests.cs) |
| Accessibility | The system EDIT control supplies native Windows accessibility. | NeoEditor exposes `TextPattern` and `TextPattern2` from the same buffer and layout the editor uses. It falls back to the basic Value provider if native text automation fails to initialize. [Windows provider](WindowsAutomation/) |
| Search and line endings | Modeless literal Find and Replace, with case and wrap options. Saving writes the text held by EDIT. | Adds regex and whole-word search, one-step Undo for Replace All, and explicit undoable CRLF, LF, or CR conversion. Ordinary saves preserve mixed separators in the buffer. [Search tests](Tests/SearchServiceTests.cs) · [File tests](Tests/FileServiceTests.cs) |
| Saves and recovery | The reference writes to the destination file in place and uses the classic save prompt on close. | Ordinary saves stage and flush data before committing and check for external file changes. Save As protects a newly created destination from an unapproved overwrite. Hard-linked files offer **Update All Links**, **Save As**, and **Cancel**. In-place updates keep a flushed recovery copy until the write succeeds. Checksummed drafts protect unsaved edits, including untitled documents, and are available through **File → Recover Drafts**. [Save tests](Tests/FileServiceTests.cs) · [Recovery tests](Tests/DraftRecoveryTests.cs) |
| Application and UI | Native Win32 menus, resources, settings, and common dialogs follow the Windows 10 design closely. | C# view models and Avalonia views implement the classic workflow with themes, a line-number gutter, basic highlighting, clickable links, and an integrated encoding picker. The Windows print dialog remains native. [Main window](Views/MainWindow.axaml) · [File picker](Views/FilePickerDialog.axaml.cs) |

The Win32 build relies on controls provided by Windows. NeoEdit ships its own editor and Avalonia's native rendering libraries. The release smoke test reports NeoEdit’s time to a focused blank window and working set on the test host. A matched benchmark would be needed to compare startup, memory, or large-file performance across builds. [Release checks](ReleaseSmokeTest.cs) · [Workload tests](Tests/EditorWorkloadTests.cs)

## Architecture

The host and editor have separate responsibilities, following the classic Notepad split. **NeoEditor** owns the piece tree, selection, layout, input, and undo. The **Notepad host** owns document paths, file encoding, save policies, dialogs, shell actions, and developer tools. A host can apply a range edit or change a display option through the control API. The same editor can also be hosted in a plain window or a compare pane. [Editor control](NeoEdit/NeoEditor.cs) · [Host commands](ViewModels/) · [Text tools](Services/Tools/)

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

## Desktop portability

Windows is the primary release target. The editor, search, encoding and line-ending controls, settings,
local file picker, and drafts use shared Avalonia and .NET code. On macOS the main menus use Avalonia's
native menu export when available, primary shortcuts use Command, and file activation opens a local path
through the same unsaved-changes workflow as Open. [Application startup](App.axaml.cs) · [Main window](Views/MainWindow.axaml.cs)

The picker lists one directory at a time and uses file metadata for its rows. Use **Show hidden files** for
dotfiles on Unix or hidden and system entries on Windows. Unix paths retain literal backslashes, colons,
and spaces. On Windows the app declares long-path awareness and keeps device names and alternate data
streams out of the normal document save workflow. [File picker](Views/FilePickerDialog.axaml.cs) ·
[Path handling](Services/DocumentPath.cs) · [Windows manifest](app.manifest)

Staged saves follow an existing file symlink and preserve the target's normal Unix permission bits.
New Unix files written by Save or Save As start with owner-only permissions. **Update All Links** is
supported on Windows and Linux. On macOS, staged saves replace the selected file entry and hard-link
aliases keep their previous contents. Drafts and settings use the user's OS data and configuration
directories. [File service](Services/FileService.cs) ·
[Draft store](Services/DraftStore.cs) · [Settings](Services/SettingsService.cs)

Native print jobs, Jump List registration, Windows shutdown hooks, and UI Automation text ranges use
Windows APIs. The custom picker provides local filesystem access on Linux. Linux desktop portals and
Unix print jobs are outside its current platform integrations.

On supported Windows 11 configurations, the main menu uses Avalonia's Mica backdrop. The native title
bar, solid editor surface, and System, Light, and Dark choices keep the same window layout on each platform.
The menu uses a solid background when Mica is unavailable or the OS requests high contrast.

On Linux, this Avalonia version uses X11 or XWayland and needs the distribution's X11 and Fontconfig
libraries. [Avalonia 11 platform requirements](https://v11.docs.avaloniaui.net/docs/overview/supported-platforms/)

## License

Released under the BSD 3-Clause license. [License text](LICENSE)
