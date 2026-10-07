#!/bin/bash
set -eu
HERE="$(cd "$(dirname "$0")" && pwd)"
EXE="$HERE/EnvGuard.app/Contents/MacOS/EnvGuard"
if [ ! -x "$EXE" ]; then
  printf '[WARNING] 未找到完整 EnvGuard.app，请保持解压目录结构。\n'
  exit 1
fi
exec "$EXE"
