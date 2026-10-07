#!/bin/bash
set -euo pipefail
SOURCE="$(cd "$(dirname "$0")" && pwd)"
OUT="${1:-$SOURCE/build}"
EXE="$OUT/EnvGuard.app/Contents/MacOS/EnvGuard"
test -x "$EXE"
/usr/bin/lipo -verify_arch arm64 x86_64 "$EXE"
/usr/bin/codesign --verify --deep --strict "$OUT/EnvGuard.app"
"$EXE" --self-test | tee "$OUT/TEST-self-test.txt"
"$EXE" --decision-self-test | tee "$OUT/TEST-decision-test.txt"
python3 "$SOURCE/TestNetwork.py" "$EXE" | tee "$OUT/TEST-network-test.txt"
TEMP="$(mktemp -d "${TMPDIR:-/tmp}/envguard-scope.XXXXXX")"
# Always operate on the exact new temporary directory, never user applications.
trap 'rm -r -- "$TEMP"' EXIT
FIXTURE="$TEMP/ScopedFixture.app"
mkdir -p "$FIXTURE/Contents/MacOS"
/usr/bin/clang "$SOURCE/TestFixture.c" -o "$FIXTURE/Contents/MacOS/EnvGuardFixture"
cat > "$FIXTURE/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>org.envguard.testfixture</string>
<key>CFBundleExecutable</key><string>EnvGuardFixture</string>
<key>CFBundlePackageType</key><string>APPL</string>
</dict></plist>
PLIST
"$EXE" --scope-fixture-test "$FIXTURE" | tee "$OUT/TEST-scope-test.txt"
