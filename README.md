# Claude Usage

A tiny macOS menu bar app (and [Windows tray app](#windows)) that shows your Claude subscription usage.

- The menu bar shows your current 5-hour session usage as a percentage.
- Click it to see current-session and weekly usage, with when each resets.
- Updates every 5 minutes, or on demand with Refresh.

## How it works

It reads the Claude Code login stored in your macOS keychain (`Claude Code-credentials`) and calls the same usage endpoint that Claude Code's `/usage` command uses. When the access token has expired, it refreshes it the way Claude Code does and writes the new token back to the keychain, so Claude Code stays logged in.

You need Claude Code installed and logged in (`claude`, then log in) at least once.

The usage endpoint isn't publicly documented, so it may change without notice.

## Build

Requires macOS 13+ and the Xcode command line tools.

```bash
./build.sh
cp -R "Claude Usage.app" /Applications/
open -a "Claude Usage"
```

On first launch, allow `security` to access "Claude Code-credentials" (choose **Always Allow**). To start it at login, add it in System Settings → General → Login Items.

## Sharing

`./build.sh` builds a universal app (Apple Silicon and Intel) and `ClaudeUsage.zip`. Send the zip.

The app isn't notarized, so macOS blocks it on the recipient's first launch. They can either:

- try to open it once, then click **Open Anyway** in System Settings → Privacy & Security, or
- run `xattr -dr com.apple.quarantine "/Applications/Claude Usage.app"`.

Each person needs Claude Code installed and logged in on their own Mac; the app shows their usage, not yours.

## Windows

The `windows/` folder has a Windows 10/11 tray app with the same features. It's a single small `ClaudeUsage.exe` built on .NET Framework 4.8, which ships with Windows, so there's nothing to install.

- The tray icon shows your 5-hour session % as a number, colored orange, then amber at 70% and red at 90%. Hover for session and weekly %.
- Left-click for the panel with session and weekly usage and reset times. Right-click for Refresh, **Start with Windows**, and Quit.
- It reads Claude Code's login from `%USERPROFILE%\.claude\.credentials.json` (or `%CLAUDE_CONFIG_DIR%`), and refreshes it like the Mac app.

Windows hides new tray icons by default. Drag it from the `^` overflow onto the taskbar to keep it visible.

### Build

From macOS or Linux with the [.NET SDK](https://dotnet.microsoft.com/download) 8.0+:

```bash
windows/build.sh
```

This writes `windows/ClaudeUsage-windows.zip`. On Windows, run `dotnet build -c Release` in `windows/`.

### Sharing

Send `ClaudeUsage-windows.zip`. The exe isn't code-signed, so SmartScreen warns on first launch: click **More info**, then **Run anyway**.
