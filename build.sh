#!/bin/zsh
# Builds a universal (Apple Silicon + Intel) "Claude Usage.app" and a
# ClaudeUsage.zip for sharing, next to this script.
set -e
cd "$(dirname "$0")"
APP="Claude Usage.app"
BIN="$APP/Contents/MacOS/ClaudeUsage"
rm -rf "$APP" ClaudeUsage.zip build
mkdir -p "$APP/Contents/MacOS" build
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
for arch in arm64 x86_64; do
  swiftc -O -parse-as-library -swift-version 5 -target "$arch-apple-macos13.0" main.swift -o "build/ClaudeUsage-$arch"
done
lipo -create build/ClaudeUsage-arm64 build/ClaudeUsage-x86_64 -output "$BIN"
rm -rf build
codesign --force --sign - "$APP"
ditto -c -k --keepParent "$APP" ClaudeUsage.zip
echo "Built $PWD/$APP ($(lipo -archs "$BIN")) and $PWD/ClaudeUsage.zip"
