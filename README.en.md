# LaunchPad v0.95

[中文](README.md) · **English**

A lightweight Windows application launcher with keyboard shortcuts, categories, folders, continuous drag and drop, customizable themes, and animated backgrounds.

## Download and run

Download from [GitHub Releases](https://github.com/lokey-t/launchPad/releases/tag/v0.95):

| Package | Requirements |
| --- | --- |
| `LaunchPad-v0.95-win-x64.zip` | Recommended. Includes the .NET runtime. Extract and run `LaunchPad.exe`. |
| `LaunchPad-v0.95-win-x64-framework-dependent.zip` | Smaller package requiring .NET 8 Desktop Runtime (x64). Keep all extracted files together. |

Supports Windows 10 / 11 x64. Exit the previous instance from its tray menu before upgrading. The default show/hide shortcut is **Ctrl + Space**, configurable in Settings. Closing the main window leaves the app in the tray; use the tray menu to exit completely.

## Features

- **Apps and folders:** add files or shortcuts by dropping them into the launcher. Search, choose single- or double-click launching, and customize display names and icons. Duplicate imports show a Toast with the existing entry and category.
- **Continuous drag and drop:** animated reordering in the main grid and folders. Drop directly onto a folder to move inside, or hover to open it and choose a position. Dragging out closes the folder and continues the same drag. Press Esc to cancel an internal drag.
- **Categories:** drag or wheel-scroll the category strip, including edge scrolling while dragging files. Reorder categories in Settings. Change an entry's category from its context menu.
- **Hover timing:** independently configure folder opening and category switching from 0.3 to 5 seconds, with gentle magnetic stops at 0.5, 1, and 2 seconds. Hovering never changes ownership before a drop.
- **Shortcuts:** manage global and category shortcuts together. Category shortcuts appear directly in their rows, with Edit and Delete actions side by side.
- **Motion and layout:** Off, Fast, Balanced, and Optimized modes; continuous folder transitions, slim rounded scrollbars, and smooth wheel scrolling. Configure icon size, window position, and automatic hiding.
- **Other:** launch at startup, tray menu, second-instance notices with the configured shortcut, and click-outside dismissal for the New Category dialog. Tab navigation and Alt access-key hints are suppressed within the app without changing Windows settings.

## Theme Center

Open from the main window or Settings → Appearance & Motion. The main-window theme button can be hidden.

1. Choose the global default or a specific category.
2. Pick Cloud, Midnight, Sea Salt, Moss, Dusk, or Rose, or edit surface, text, and accent colors.
3. Upload a background and adjust its overlay. Supported formats include PNG / JPG / BMP, GIF, and MP4 / WMV / AVI / MOV / M4V video. Video playback depends on Windows decoding support.
4. Apply and save. Category colors and backgrounds independently override or inherit global settings.

Window Material is independent of the palette: Normal, Frosted Glass, or Liquid Glass, with separate per-category inheritance. Glass uses the Windows compositor to blur live desktop and window content behind the launcher while keeping text and icons sharp. Liquid mode combines lighter tint and reflective highlights as an approximation, not Apple’s native refraction. Background upload, background override, and overlay controls are disabled while glass is active; existing media is retained and restored in Normal mode. Move the Theme Center window to see the live material preview. Effects depend on Windows composition and transparency support.

Colors and backgrounds transition smoothly between categories. Videos loop silently; changing colors or the overlay for the same video preserves playback. GIF and video playback pause while hidden. Invalid media produces a message and falls back to the theme surface.

## Configuration and data

- Settings live in `%AppData%/LaunchPad/config.json`; upgrading does not require copying the old application directory.
- Uploaded icons and backgrounds are copied to `%AppData%/LaunchPad/Icons` and `Backgrounds`, so moving the original file does not break them.
- Renaming changes the display name only. Removing entries or categories does not delete disk files.
- Click Done in Settings to save. Category reordering updates the main window when the drag is released.

## Build from source

Requires Windows and the .NET 8 SDK. No third-party NuGet dependencies.

```powershell
dotnet build LaunchPad.csproj -c Release
dotnet publish LaunchPad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o bin/publish
```

Exit any instance using the output directory before rebuilding, or choose a different directory.

| Source | Purpose |
| --- | --- |
| `MainWindow.*` | Launcher, drag sessions, folder transitions, category scrolling, and themes |
| `SettingsWindow.*` | Settings, category sorting, shortcuts, and magnetic sliders |
| `ThemeCenterWindow.cs` / `Controls` | Theme editing and background media |
| `Models` / `Services` | Configuration, hotkeys, entry moves, icons, themes, and motion |
| `Themes` / `Converters` | WPF styles, resources, and binding converters |

See [release notes](RELEASE_NOTES.md). A SHA-256 checksum file accompanies the release.
