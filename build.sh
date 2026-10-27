#!/bin/zsh
# Builds "Claude Usage.app" next to this script.
set -e
cd "$(dirname "$0")"
APP="Claude Usage.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
cat > "$APP/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Claude Usage</string>
  <key>CFBundleIdentifier</key><string>local.claude-usage</string>
  <key>CFBundleExecutable</key><string>ClaudeUsage</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
</dict></plist>
EOF
swiftc -O -parse-as-library -swift-version 5 main.swift -o "$APP/Contents/MacOS/ClaudeUsage"
codesign --force --sign - "$APP"
echo "Built $PWD/$APP"
