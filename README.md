# Claude Usage

A tiny macOS menu bar app that shows your Claude subscription usage.

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
