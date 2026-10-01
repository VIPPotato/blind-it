#!/usr/bin/env bash
# Test the menu reader's text logic outside the game.
#
# Why this exists: an in-game test costs the player several minutes, so anything
# provable on the desktop is proved here first. This compiles the mod's REAL
# LabelText.cs, which decides whether the player hears "Play" or hears TMP markup
# spelled out.
#
# Usage:
#   bash tools/label-tests.sh
#
# Exit code 0 means every test passed.

set -euo pipefail

# pwd -W gives a NATIVE Windows path. A plain MSYS path like /d/Documents/... is
# not translated for native tools such as dotnet, which reads it as an unknown
# MSBuild switch and fails the build.
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -W)"
PROJ="$HERE/LabelTests/LabelTests.csproj"
OUTDIR="$HERE/LabelTests/bin/Release/net6.0"

echo "== label text tests =="
echo "building..."
dotnet build "$PROJ" -c Release -v minimal

echo
"$OUTDIR/LabelTests.exe"
