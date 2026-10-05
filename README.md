# ArkaSoft Notepad

A fast, lightweight text editor for Windows with a modern **Windows 11** style interface — built with **WPF** and **.NET 10**.

ArkaSoft Notepad is designed to replace the classic Windows Notepad while adding the features you actually need: tabs, a Visual Studio Code style file explorer, full right-to-left (Persian/Arabic) support, themes, advanced find & replace, text tools, and much more.

> Developed by **Saeed Rajabi** — **ArkaSoftware** · [https://arkasoftware.ir](https://arkasoftware.ir)

---

## Features

### Editing
- **Multi-tab editing** — open many files in one window; unsaved tabs are marked, middle-click closes a tab
- **Plain-text editing core** with smart Persian/English paragraph direction
- **Find & Replace** with live match highlighting and a result counter
- **Go to Line** (Ctrl+G), **Time/Date** insertion (F5), emoji panel
- **Text tools** — UPPERCASE / lowercase / Title Case, trim trailing spaces, sort lines (A→Z, Z→A), remove duplicate lines
- **Line numbers** gutter, **zoom** (10 % – 500 %, Ctrl + Mouse Wheel), word wrap, status bar with Ln/Col, line and character counts, and encoding

### File handling
- **Explorer sidebar** (VS Code style): open a working folder, create files/folders inline, rename (with extension editing), delete, reveal in Windows Explorer; colorful per-extension file icons; pin, auto-hide, or close — and resize it
- **File associations** — become the default editor for `.txt`, `.log`, `.md`, `.ini`, `.cfg`, `.csv` and show the app logo on those files in Explorer
- **Session restore** — unsaved work is autosaved as drafts and restored on the next launch
- **Recent files** menu and drag-and-drop support
- **Single instance** — opening files from Explorer joins the running window as new tabs
- **Printing** with page setup (paper size, orientation, margins) and **text encodings**: UTF-8, UTF-8 with BOM, UTF-16 LE/BE, ANSI

### Interface
- **Light and Dark themes** with automatic Windows theme detection
- **Bilingual UI** — English and Persian, with full right-to-left layout
- **Bilingual fonts** — separate Persian (Vazir) and English (Cascadia Mono) editor fonts, configurable in-app
- Custom Windows 11 style title bar, menus, dialogs, and context menus

---

## Keyboard Shortcuts

| Action | Shortcut |
|---|---|
| New tab / Close tab | `Ctrl+T` / `Ctrl+W` |
| New window | `Ctrl+Shift+N` |
| Open / Save / Save as | `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` |
| Find / Replace | `Ctrl+F` / `Ctrl+H` |
| Find next / previous | `F3` / `Shift+F3` |
| Go to line | `Ctrl+G` |
| Explorer sidebar | `Ctrl+B` |
| Next / previous tab | `Ctrl+Tab` / `Ctrl+Shift+Tab` |
| Zoom in / out / reset | `Ctrl+Plus` / `Ctrl+Minus` / `Ctrl+0` |
| UPPERCASE / lowercase | `Ctrl+Shift+U` / `Ctrl+Shift+L` |
| Insert time/date | `F5` |

---

## Building

Requirements: **Windows 10/11** and the **.NET 10 SDK**.

```bash
# restore + build the whole solution
dotnet build ArkaSoft.Notepad.slnx -c Release

# run from source
dotnet run --project Src/ArkaSoft.Notepad.UI

# publish a single-folder release (framework-dependent)
dotnet publish Src/ArkaSoft.Notepad.UI/ArkaSoft.Notepad.UI.csproj -c Release -r win-x64 --self-contained false

# or self-contained (no runtime needed on the target PC)
dotnet publish Src/ArkaSoft.Notepad.UI/ArkaSoft.Notepad.UI.csproj -c Release -r win-x64 --self-contained true
```

The Windows installer assets (license, before/after install notes) live in the [`Setup`](./Setup) folder and are consumed by the Inno Setup script:

```ini
[Setup]
LicenseFile=Setup\License.txt
InfoBeforeFile=Setup\InfoBefore.txt
InfoAfterFile=Setup\InfoAfter.txt
```

---

## Project Structure

```
ArkaSoft.Notepad/
├── ArkaSoft.Notepad.slnx
├── Setup/                        # Inno Setup license & info files
├── Scripts/                      # build helper scripts (icon generation)
└── Src/ArkaSoft.Notepad.UI/
    ├── Controls/                 # FindReplacePanel, SidebarPanel (Explorer)
    ├── Dialogs/                  # custom message/input/rename/encoding/… dialogs
    ├── Helpers/                  # commands, RTL text engine, outline, icons
    ├── Models/                   # DocumentTab, FileSystemNode
    ├── Services/                 # settings, themes, localization, file/single-instance/print…
    ├── Themes/                   # light/dark palettes + Win11 control styles
    └── Assets/                   # app logo, fonts
```

## User Data

| Data | Location |
|---|---|
| Settings | `%APPDATA%\ArkaSoft.Notepad\settings.json` |
| Session & unsaved drafts | `%APPDATA%\ArkaSoft.Notepad\session\` |
| Error log | `%APPDATA%\ArkaSoft.Notepad\error.log` |

## Code Protection

Release builds are automatically obfuscated: private/internal symbols are
renamed, string constants are encrypted, debug symbols are stripped, and the
protected binary replaces the normal build output. See
[Docs/CodeProtection.md](./Docs/CodeProtection.md) for details and limits.

```bash
# recommended distribution build (single compressed exe, obfuscated)
dotnet publish Src/ArkaSoft.Notepad.UI/ArkaSoft.Notepad.UI.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

---

## License

ArkaSoft Notepad is freeware — free for personal and commercial use. See [Setup/License.txt](./Setup/License.txt) for the full terms.

## About

**ArkaSoft Notepad** is designed and developed by **Saeed Rajabi** as part of the **ArkaSoftware** development team.

- Website: [https://arkasoftware.ir](https://arkasoftware.ir)
- Developer: Saeed Rajabi
