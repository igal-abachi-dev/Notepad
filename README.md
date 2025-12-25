# Notepad
Notepad (classic) written in C# + Avalonia UI + Native AOT 


in windows 11 , notepad changed from being light single exe (c++ win32 based on windows messages and gdi and com dialogs),
into c++ winrt(uwp/winui xaml) with richtext markdown , tabs , restart manager , cowriter ai.... and more bloat
but they removed the simplicity it had , and made it slower load and heavy

so this is remaking the classic notepad on modern stack so its notepad xaml ui , more maintainable

text engine is based of piece Red-Black Tree of stringbuilder text buffer like vs code / vs 2026 editor
instead of ropes<T> of avalonia edit 

and uses textLayout for rendering text

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

## 🛠️ Build
Requirements: .NET 8.0 SDK or higher.

```bash
# Clone
git clone https://github.com/igal-abachi-dev/Notepad.git

# Run
dotnet run --project NotepadAvalonia

# Publish (Native AOT)
dotnet publish -c Release -r win-x64
```

