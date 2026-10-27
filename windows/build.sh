#!/bin/zsh
# Builds ClaudeUsage.exe (.NET Framework 4.8) and ClaudeUsage-windows.zip for sharing.
# Works on macOS or Linux with the .NET SDK (8.0+) installed; on Windows, run `dotnet build -c Release`.
set -e
cd "$(dirname "$0")"
DOTNET=$(command -v dotnet || echo "$HOME/.dotnet/dotnet")
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
rm -rf bin obj ClaudeUsage-windows.zip
"$DOTNET" build -c Release -nologo -v quiet
cp bin/Release/net48/ClaudeUsage.exe .
zip -q ClaudeUsage-windows.zip ClaudeUsage.exe
rm ClaudeUsage.exe
echo "Built $PWD/bin/Release/net48/ClaudeUsage.exe and $PWD/ClaudeUsage-windows.zip"
