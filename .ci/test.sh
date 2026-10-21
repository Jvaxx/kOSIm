#!/usr/bin/env bash
# Run local headless checks, independent of the caller's cwd.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
python="${PYTHON:-python3}"
"$python" -m unittest discover -s .ci -p 'test_*.py' -v
"$python" .ci/check-version.py
dotnet msbuild src/kOS.Safe.Test/kOS.Safe.Test.csproj \
    -restore -target:RunHeadlessTests -property:KOSHeadless=true \
    -property:Configuration=Release -property:PythonExecutable="$python" -nologo
