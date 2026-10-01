# Blind it!

A screen reader mod for Bop It! The Video Game. It speaks the menus, the gameplay
prompts, the scores and the achievements through your screen reader, so the game can be
played without sight.

Tested with NVDA on Windows.

## Installing

1. Install MelonLoader into the game folder first, the folder holding `BopIt!.exe`.
   Blind it! is a MelonLoader mod and will not load without it. Download it from the
   official releases page:

   https://github.com/LavaGang/MelonLoader/releases/latest

   Take `MelonLoader.Installer.exe`, run it, point it at `BopIt!.exe` and choose the
   64-bit version. Blind it! is built and tested against MelonLoader v0.7.3 x64. If you
   prefer step-by-step instructions, MelonLoader's own guide is at https://melonwiki.xyz
2. Download the release zip and extract it somewhere, such as your Downloads folder.
3. Open the extracted folder. Inside are three folders: `Mods`, `UserData` and
   `UserLibs`.
4. Select all three and copy them.
5. Paste them into the game folder. Windows will ask whether to merge the folders
   already there; allow it. Nothing is overwritten except this mod's own files.
6. Start the game. The mod announces itself once it is ready.

The zip includes a `UserData\Loader.cfg` that keeps MelonLoader's console window and
splash screen from opening, because either one can steal focus from a screen reader.

## Languages

The mod speaks English, German, Spanish, Latin American Spanish, French, Italian,
Japanese, Korean, Brazilian Portuguese and Simplified Chinese.

It follows the game's own language setting. To force one language, open
`Mods\BlindIt-language.txt` and replace `auto` with a language code: `en`, `de`, `es`,
`es-mx`, `fr`, `it`, `ja`, `ko`, `pt-br` or `zh`.

Translations live in `Mods\Localization\<code>.json`. To correct a translation, edit that
file. Do not edit `en.json`: the mod rewrites it on every start. To add a language, copy
`en.json` to a new file named for the language code and translate the values, leaving the
names on the left of each colon and any `{0}` markers exactly as they are.

## Keys

- Backslash: repeat the last thing spoken
- Left bracket and right bracket: move through the current screen's items
- Up arrow and down arrow: move through a list
- F8: write a diagnostic dump to the log, for reporting a problem

The game's own keys are unchanged.

## Reporting a problem

The mod writes `Mods\BlindIt.log` inside the game folder. It records what the mod saw
and said, which is usually enough to find the cause. Press F8 before quitting to add a
snapshot of the mod's state, then include the log in your report.

## Credits and licence

Blind it! is MIT licensed; see `LICENSE`. That covers the mod's own source and
documentation only; the bundled third-party pieces keep their own licences.

Speech reaches the screen reader through Prism, redistributed under the Mozilla Public
License 2.0. Its licence and NOTICE are in `UserLibs\prism-licence` in the download, and
in `lib\prism` and `dist\UserLibs` in the source tree.

MelonLoader (https://github.com/LavaGang/MelonLoader) is Apache-2.0 licensed and is not
included in this download. You install it yourself in step 1.

## Building it yourself

You need the .NET SDK, MelonLoader already installed in your game folder, and one run of
the game so MelonLoader generates the proxy assemblies the mod compiles against. Those
assemblies are generated from the game's own code, so they are never committed here.

The build looks for the game in `L:\SteamLibrary\steamapps\common\Bop It!`. Yours is
almost certainly elsewhere, so point it at your own copy:

```
GAME_DIR="D:/Steam/steamapps/common/Bop It!" bash deploy.sh
```

That builds the mod and copies it into your game folder.

This mod contains no code or assets from the game. It is an independent accessibility
modification and is not affiliated with or endorsed by the game's developers or
publisher.
