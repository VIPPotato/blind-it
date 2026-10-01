#!/usr/bin/env bash
# Run the mod's real speech code outside the game.
#
# Why this exists: an in-game test costs the player several minutes, so a
# binding, encoding or backend fault should be caught here first. The harness
# compiles the SAME Speech.cs, Paths.cs and Log.cs the mod ships.
#
# It will speak out loud through whatever screen reader is running.
#
# Usage:
#   bash tools/speech-harness.sh           speak the two sentences and check them
#   bash tools/speech-harness.sh --quiet   check the UTF-8 conversion, say nothing

set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -W)"
PROJ="$HERE/SpeechHarness/SpeechHarness.csproj"
OUTDIR="$HERE/SpeechHarness/bin/Release/net6.0"
PRISM="$(cd "$HERE/../lib/prism" && pwd -W)/prism.dll"
GAME_DIR="${GAME_DIR:-L:/SteamLibrary/steamapps/common/Bop It!}"

echo "== speech harness =="

if [ ! -f "$PRISM" ]; then
  echo "ERROR: $PRISM is missing. Run get-prism.ps1 first." >&2
  exit 1
fi

echo "building..."
dotnet build "$PROJ" -c Release -v minimal

# Speech.cs looks for prism.dll in <game>\UserLibs and then next to itself.
# Outside the game the first path still resolves (the game is installed), but
# copy it next to the exe too so the harness works even if the game folder moves.
cp -f "$PRISM" "$OUTDIR/prism.dll"
echo "prism.dll staged next to the harness"

# Log.cs references MelonLoader.dll. Inside the game the loader provides it;
# out here it has to sit next to the exe or the reference cannot resolve.
# Log.Line survives a missing MelonLoader by design, but resolving it means the
# harness exercises the same code path the game does.
if [ -f "$GAME_DIR/MelonLoader/net6/MelonLoader.dll" ]; then
  cp -f "$GAME_DIR/MelonLoader/net6/MelonLoader.dll" "$OUTDIR/MelonLoader.dll"
  echo "MelonLoader.dll staged next to the harness"
else
  echo "note: MelonLoader.dll not found; testing the missing-loader path instead"
fi
echo

"$OUTDIR/SpeechHarness.exe" "$@"
