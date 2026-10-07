#!/bin/bash
set -euo pipefail
SOURCE="$(cd "$(dirname "$0")" && pwd)"
OUT="${1:-$SOURCE/build}"
mkdir -p "$OUT"
for arch in arm64 x86_64; do
  /usr/bin/swiftc -O -target "$arch-apple-macos14.0" "$SOURCE/Core.swift" "$SOURCE/DecisionTests.swift" "$SOURCE/main.swift" -framework AppKit -framework CryptoKit -o "$OUT/EnvGuard-$arch"
done
APP="$OUT/EnvGuard.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
/usr/bin/lipo -create "$OUT/EnvGuard-arm64" "$OUT/EnvGuard-x86_64" -output "$APP/Contents/MacOS/EnvGuard"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>EnvGuard</string>
<key>CFBundleIdentifier</key><string>org.envguard.macos</string>
<key>CFBundleName</key><string>EnvGuard</string>
<key>CFBundleDisplayName</key><string>环境预警检测器</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>1.2.8</string>
<key>CFBundleVersion</key><string>1282</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSRequiresAquaSystemAppearance</key><true/>
<key>CFBundleIconFile</key><string>EnvGuard</string>
<key>NSHumanReadableCopyright</key><string>MIT · EnvGuard contributors · macOS adaptation</string>
</dict></plist>
PLIST
if [ -f "$SOURCE/EnvGuard.icns" ]; then cp "$SOURCE/EnvGuard.icns" "$APP/Contents/Resources/EnvGuard.icns"; fi
chmod 755 "$APP/Contents/MacOS/EnvGuard"
/usr/bin/codesign --force --sign - "$APP"
/usr/bin/codesign --verify --deep --strict "$APP"
printf '[OK] Universal macOS 14+ app: %s\n' "$APP"
