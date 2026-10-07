#!/bin/bash
set -euo pipefail
SOURCE="$(cd "$(dirname "$0")" && pwd)"
OUT="${1:-$SOURCE/build}"
OUT="$(cd "$OUT" && pwd)"
test -x "$OUT/EnvGuard.app/Contents/MacOS/EnvGuard"
for name in self decision network scope; do test -s "$OUT/TEST-$name-test.txt"; done
/usr/bin/codesign --verify --deep --strict "$OUT/EnvGuard.app"
STAGE="$(mktemp -d "${TMPDIR:-/tmp}/envguard-package.XXXXXX")"
trap 'rm -r -- "$STAGE"' EXIT
PACKAGE="$STAGE/EnvGuard_macOS_Release"
mkdir -p "$PACKAGE/NativeSource"
cp -R "$OUT/EnvGuard.app" "$PACKAGE/EnvGuard.app"
cp "$SOURCE/环境预警检测器.command" "$PACKAGE/"
chmod 755 "$PACKAGE/环境预警检测器.command"
for name in Core.swift main.swift DecisionTests.swift TestNetwork.py TestFixture.c build.sh test.sh package.sh README.md MIGRATION_AUDIT.md DEPENDENCIES.txt UPSTREAM_COMMIT.txt EnvGuard.icns LAN-DOWNLOAD.md; do
  cp "$SOURCE/$name" "$PACKAGE/NativeSource/"
done
chmod 755 "$PACKAGE/NativeSource/"*.sh
cp "$SOURCE/README.md" "$SOURCE/MIGRATION_AUDIT.md" "$SOURCE/DEPENDENCIES.txt" "$SOURCE/LAN-DOWNLOAD.md" "$PACKAGE/"
if [ -f "$SOURCE/LICENSE" ]; then
  cp "$SOURCE/LICENSE" "$PACKAGE/LICENSE"
else
  cp "$SOURCE/../../LICENSE" "$PACKAGE/LICENSE"
fi
cp "$PACKAGE/LICENSE" "$PACKAGE/NativeSource/LICENSE"
cp "$OUT/TEST-"*.txt "$PACKAGE/"
COMMIT="$(git -C "$SOURCE" rev-parse HEAD 2>/dev/null || printf 'source-only')"
printf 'EnvGuard macOS 1.2.8-macos.2\nCommit: %s\nBuilt/tested host: %s / macOS %s\nUniversal 2: arm64 + x86_64; deployment target macOS 14.0\nSigning: ad-hoc, not Developer ID notarized\nNo personal configuration or real audit logs included\n' "$COMMIT" "$(uname -m)" "$(sw_vers -productVersion)" > "$PACKAGE/BUILD_INFO.txt"
(cd "$PACKAGE"; find . -type f ! -name SHA256SUMS.txt -exec shasum -a 256 {} \; > SHA256SUMS.txt)
DEST="$(dirname "$OUT")/EnvGuard_macOS_Release.zip"
/usr/bin/ditto -c -k --keepParent "$PACKAGE" "$DEST"
shasum -a 256 "$DEST" | sed 's|  .*/|  |' > "$(dirname "$OUT")/SHA256SUMS-macos.txt"
printf '[OK] Portable macOS package: %s\n' "$DEST"
