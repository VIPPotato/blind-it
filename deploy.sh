#!/usr/bin/env bash
# Build BlindIt and copy it into the game's Mods folder.
#
# Used by every stage of this project. It refuses to run while the game is
# open, because Windows locks a loaded DLL: the copy would be skipped and the
# next launch would silently test the OLD build.
#
# Usage:  bash deploy.sh
#
# Your game folder is probably somewhere else. Override it without editing this
# file:  GAME_DIR="D:/Steam/steamapps/common/Bop It!" bash deploy.sh

set -euo pipefail

GAME_DIR="${GAME_DIR:-L:/SteamLibrary/steamapps/common/Bop It!}"
# pwd -W gives a NATIVE Windows path with forward slashes. A plain MSYS path
# like /d/Documents/... is not translated for native tools such as dotnet,
# which then rejects it as an unknown MSBuild switch.
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/src/BlindIt" && pwd -W)"
MODS_DIR="$GAME_DIR/Mods"
OUTPUT="$PROJECT_DIR/bin/Release/net6.0/BlindIt.dll"

echo "== BlindIt deploy =="

# 1. Refuse to deploy while the game runs.
if tasklist 2>/dev/null | grep -i -q "BopIt"; then
  echo "ERROR: Bop It! is running. Quit the game first, then run this again." >&2
  exit 1
fi
echo "game not running: ok"

# 2. Sanity-check the loader is installed.
if [ ! -f "$GAME_DIR/version.dll" ]; then
  echo "ERROR: $GAME_DIR/version.dll is missing. MelonLoader is not installed." >&2
  exit 1
fi
echo "MelonLoader proxy present: ok"

# 3. Build.
echo "building..."
dotnet build "$PROJECT_DIR/BlindIt.csproj" -c Release -v minimal

if [ ! -f "$OUTPUT" ]; then
  echo "ERROR: build produced no $OUTPUT" >&2
  exit 1
fi

# 4. Copy.
mkdir -p "$MODS_DIR"
cp -f "$OUTPUT" "$MODS_DIR/BlindIt.dll"

# 4a. Remove the mod's old name.
#
# The mod was called BopItAccess until 2026-10-01. MelonLoader loads EVERY dll in
# Mods, so leaving the old file there runs two copies of the mod at once: both
# speak, and the player hears every line twice. This is why the rename deletes
# rather than leaves it.
for stale in "$MODS_DIR/BopItAccess.dll"; do
  if [ -f "$stale" ]; then
    rm -f "$stale"
    echo "removed old build: $stale"
  fi
done

# 4b. Ship the translations.
#
# The mod writes en.json itself at startup, but the repository's files are the
# ones contributors edit, so they are copied in.
#
# cp -f, not cp -n: this used to be cp -n, which NEVER overwrites. That silently
# left an older translation in the game folder, so a corrected string looked
# deployed but the next launch still spoke the stale one. The repository is the
# source of truth, so a deploy must win. To try a translation edit without
# committing it, edit the repository copy and deploy again, or use the mod's
# reload hotkey.
LOCALE_SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -W)/Localization"
if [ -d "$LOCALE_SRC" ]; then
  mkdir -p "$MODS_DIR/Localization"
  cp -f "$LOCALE_SRC"/*.json "$MODS_DIR/Localization/" 2>/dev/null || true
  echo "translations: $(ls -1 "$MODS_DIR/Localization"/*.json 2>/dev/null | wc -l) file(s) in $MODS_DIR/Localization"
fi

# 4c. Refresh the release tree.
#
# dist/ is what the GitHub workflow zips, so the DLL and translations have to
# match what was just built. Keeping them in step here means a release can never
# ship an older DLL or an older translation than the one that was tested in game.
#
# Loader.cfg is deliberately NOT copied from the game folder. It used to be, and
# that was backwards: the game's copy is the player's local file, and MelonLoader
# rewrites it on update. One local change to hide_console there would have
# silently travelled into the release tree, and every downloader would get a
# console window. dist/UserData/Loader.cfg is version-controlled and is the
# source of truth, so this only CHECKS it and refuses to continue if the two
# flags the release depends on are not set.
DIST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -W)/dist"
if [ -d "$DIST_DIR" ]; then
  mkdir -p "$DIST_DIR/Mods/Localization"
  cp -f "$OUTPUT" "$DIST_DIR/Mods/BlindIt.dll"
  if [ -d "$LOCALE_SRC" ]; then
    cp -f "$LOCALE_SRC"/*.json "$DIST_DIR/Mods/Localization/" 2>/dev/null || true
  fi

  DIST_CFG="$DIST_DIR/UserData/Loader.cfg"
  if [ ! -f "$DIST_CFG" ]; then
    echo "ERROR: $DIST_CFG is missing. The release zip needs it." >&2
    exit 1
  fi
  for flag in hide_console disable_start_screen; do
    if ! grep -qE "^[[:space:]]*$flag[[:space:]]*=[[:space:]]*true" "$DIST_CFG"; then
      echo "ERROR: $DIST_CFG does not set $flag = true." >&2
      echo "       Releasing it would pop a console window for every player." >&2
      exit 1
    fi
  done

  echo "release tree refreshed: $DIST_DIR (Loader.cfg flags ok)"
fi

echo "copied:"
echo "  $OUTPUT"
echo "    -> $MODS_DIR/BlindIt.dll"
ls -la "$MODS_DIR/BlindIt.dll"

echo "== deploy done =="
