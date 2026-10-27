# Claude Usage

A tiny menu bar / system tray app for **macOS** and **Windows** that shows your Claude subscription usage at a glance.

- Your current 5-hour session usage, always visible as a percentage.
- Click for current-session and weekly usage, with when each resets.
- Updates every 5 minutes, or on demand with Refresh.

## Download

Get the latest version from **[Releases](https://github.com/shhc1489/claude-usage/releases/latest)**:

- **macOS:** `ClaudeUsage-macos.zip`. Unzip, drag **Claude Usage** into Applications, and open it. The first time, macOS blocks it: go to System Settings → Privacy & Security → **Open Anyway**.
- **Windows:** `ClaudeUsage-windows.zip`. Unzip and run `ClaudeUsage.exe`. If SmartScreen appears, click **More info** → **Run anyway**.

No build tools needed. To build from source instead, see [macOS](#macos) or [Windows](#windows) below.

| | macOS | Windows |
|---|---|---|
| Runs on | macOS 13+ (Apple Silicon and Intel) | Windows 10 and 11 |
| Lives in | Menu bar: icon + `42%` | System tray: `42` drawn in the icon |
| Built with | Swift / SwiftUI | C# / WinForms on .NET Framework 4.8 |
| Download size | ~130 KB zip | ~15 KB zip |
| Source | [`main.swift`](main.swift), [`build.sh`](build.sh) | [`windows/`](windows) |

## Requirements

- [Claude Code](https://claude.com/claude-code) installed and logged in (`claude`, then log in) at least once, on the same computer.
- A Claude subscription (Pro or Max).

Each person sees their own usage. The app contains no account details of its own.

## How it works

Both versions read the login Claude Code already saved on your computer and call the same usage endpoint that Claude Code's `/usage` command uses.

| | Where the login is read from |
|---|---|
| macOS | Keychain item `Claude Code-credentials` |
| Windows | `%USERPROFILE%\.claude\.credentials.json` (or `%CLAUDE_CONFIG_DIR%`) |

When the access token has expired, the app refreshes it the way Claude Code does and saves the new token back, so Claude Code stays logged in.

The usage endpoint isn't publicly documented, so it may change without notice.

## macOS

### Build and install

Requires the Xcode command line tools.

```bash
./build.sh
cp -R "Claude Usage.app" /Applications/
open -a "Claude Usage"
```

On first launch, allow `security` to access "Claude Code-credentials" (choose **Always Allow**). To start it at login, add it in System Settings → General → Login Items.

### Sharing

`./build.sh` builds a universal app (Apple Silicon and Intel) and `ClaudeUsage.zip`. Send the zip.

The app isn't notarized, so macOS blocks it on the recipient's first launch. They can either:

- try to open it once, then click **Open Anyway** in System Settings → Privacy & Security, or
- run `xattr -dr com.apple.quarantine "/Applications/Claude Usage.app"`.

## Windows

A single small `ClaudeUsage.exe`. .NET Framework 4.8 ships with Windows 10 and 11, so there's nothing to install.

- The tray icon shows your session % as a number, orange normally, amber at 70% and red at 90%. Hover for session and weekly %.
- Left-click for the usage panel. Right-click for Refresh, **Start with Windows**, and Quit.
- Windows hides new tray icons by default. Drag it from the `^` overflow onto the taskbar to keep it visible.

### Build

From macOS or Linux with the [.NET SDK](https://dotnet.microsoft.com/download) 8.0+:

```bash
windows/build.sh
```

This writes `windows/ClaudeUsage-windows.zip`. On Windows, run `dotnet build -c Release` in `windows/`.

### Sharing

Send `ClaudeUsage-windows.zip`. The exe isn't code-signed, so SmartScreen warns on first launch: click **More info**, then **Run anyway**.
