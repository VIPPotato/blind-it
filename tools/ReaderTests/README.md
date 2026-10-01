# Game-free ScreenReader scheduling regressions

This console harness compiles the **complete, unmodified production ScreenReader.cs**
with lightweight managed Unity/IL2CPP stand-ins. It does not load the game, its
assemblies, the mod loader, a speech DLL, or NVDA. No production source or existing
suite is modified.

## Run from the project root

Working directory:

`D:/Documents/projects/games/bop it the video game`

Prove that the unchanged shipped reader fails the relevant behavioral checks:

```bash
python tools/ReaderTests/run.py baseline --expect-red
```

Check the current production reader:

```bash
python tools/ReaderTests/run.py current
```

The runner invokes **C:/Program Files/dotnet/dotnet.exe**, not a WSL dotnet.
It targets the installed net8.0 reference pack/runtime. Restore always includes
`--source 'C:/program files/dotnet/packs' --ignore-failed-sources`, with NuGet audit
disabled. There are no PackageReference, ProjectReference, game assembly or Unity
assembly references. No installation or download step exists.

`--expect-red` only accepts a successful build followed by exactly the five known
assertion failures and no harness errors. The underlying baseline executable
returns **1**; the wrapper returns **0** only after proving that this is the
expected red control. A restore/build/runtime/receipt error is never accepted as
a regression failure. Without `--expect-red`, test assertion failures return 1;
harness errors return 2; a green run returns 0.

## Source selection and artifacts

- `ModSrc` is an MSBuild property selecting the directory containing the actual
  production source files. Default: `../../src/BlindIt`, relative to this project.
- `ReaderSource` optionally overrides only ScreenReader.cs. It defaults to
  `$(ModSrc)/ScreenReader.cs`.
- The runner's `baseline` variant sets ModSrc to
  `evidence/launch9/baseline/src/BlindIt`. It checks the immutable reader's path and
  SHA-256 before building; it never edits or generates the baseline.
- The `current` variant links `src/BlindIt/ScreenReader.cs` and its real helpers.
- LabelText.cs, Announcement.cs and Strings.cs are always linked from ModSrc.
  LeaderboardWindow.cs is linked if present; the baseline predates this helper.
- Source is not extracted, copied into fake implementations, preprocessed, or
  conditionally rewritten. The helper fakes contain no reader scheduling logic.
- The runner supports `--reader-source PATH` for current-source experiments. For
  direct dotnet calls, pass `-p:ModSrc=...` and/or `-p:ReaderSource=...` consistently
  to both restore and build.
- Separate build roots prevent baseline/current reuse:
  `tools/ReaderTests/bin/<variant>/Release/net8.0/ReaderTests.dll` and
  `tools/ReaderTests/obj/<variant>/`.
- Evidence is under `evidence/launch9/reader-tests/<variant>/`:
  `commands.txt`, `restore.log`, `build.log`, `tests.log`, `results.json`, and
  `run-manifest.json`. The commands file contains the exact expanded native
  dotnet restore/build/run commands with Bash-compatible quoting.
- Each executable embeds the linked sources as provenance resources. The runner
  checks those hashes against build inputs, rejects changes during the build,
  validates enumerated test totals, and records the assembly hash. Later parent
  edits do not make an earlier green result proof of the new source bytes.
- CLI-home/cache paths used by the runner are local to tools/ReaderTests;
  telemetry and workload-update notifications are disabled for its child process.

## What the twelve cases exercise

All frames and input pulses run **production ScreenReader.Tick()**. The fake
speech boundary records every call, text, frame, input kind, and interrupt flag;
it never suppresses, queues, reformats, or deduplicates speech.

- Start -> Warmup -> Calibrate -> Result -> Start, with sixty idle Tick calls
  after every visit: exactly five automatic utterances, in exact English order.
- The old duplicate boundary: one utterance at +0, +29, +30 and +60 idle frames
  after a context change, not a second call at +30.
- Settled, repeatedly unchanged stages stay silent, including Finished.
- A genuine return to Start still announces immediately.
- Repeated manual repeat commands remain audible even with identical words.
- Repeat immediately after a context change re-reads through production
  Repeat -> Resnapshot -> ReadItems rather than relying on the old snapshot.
- Calibration entry, repeat and review have no spoken list positions.
- Real countdown and delay review lines remain reachable.
- Calibration item counts are suppressed, but ordinary list counts remain.
- An empty leaderboard fills once within thirty frames, retaining its spoken
  row position; subsequent non-empty updates do not interrupt the reader.
- Closing/reopening the controlled fake calibration panel announces Start again.

Reflection is limited to fixture reset, required-member checks, and the real
CountItems helper. Missing/changed required members are errors, never skipped
cases. ReadItems is not invoked by a hardcoded reflection signature, so the
baseline's two-argument version and current optional-third-argument version both
compile. The current production CaptureItems/LeaderboardWindow path is exercised
by the leaderboard case.

## Results captured when authored

- Baseline: restore/build succeeded; 0 warnings and 0 compiler errors;
  **7 passed, 5 failed, 0 harness errors**, executable exit 1.
- Current tested reader: restore/build succeeded; 0 warnings and 0 compiler
  errors; **12 passed, 0 failed, 0 harness errors**, executable exit 0.
- The baseline cycle emitted 9 calls instead of 5. Its Warmup call at frame 71
  repeated at frame 101, with `1 of 2` added. The current cycle emitted only
  frames 10, 71, 132, 193, 254.
- Both restore assets files contain zero package libraries and only the local
  installed-pack source.
- Exact source hashes and transcripts are in the evidence, not inferred from
  timestamps. Current source continues to be edited by the parent; rerun it
  after integration.

## Coverage limits and later integration

Fakes provide ordinary managed objects, an explicit registry/hierarchy and test
flags. Their visibility/liveness, casts, input flags, and estimated latency are
**controlled inputs**, not proof of real Unity visibility, native object lifetime,
IL2CPP signatures, focus routing, input binding, Harmony hooks, deployment, audible
NVDA output, interruption timing, or usefulness to the player. Only the player
launches and tests the game.

Achievement/track discovery code compiles but is not behaviorally covered by
these calibration tests. LeaderboardWindow is real source, but this suite only
checks empty-to-filled scheduling and positions, not all rank-gap rules. The
recorded current late-fill call has `interrupt=false`; the fake does not simulate
whether real speech is cut off.

If later production edits introduce new game members, extend Fakes.cs from those
actual uses. If a new pure production helper is needed, add a real Compile link
and matching EmbeddedResource; do not copy its logic into a fake. A helper absent
from the immutable baseline can be linked conditionally, as LeaderboardWindow is.
Update the runner's explicit optional-source allowlist accordingly. Do not modify
production code merely to satisfy this harness.

No NOTEBOOK.md or Kanban state was edited: this delegated task owns only this
directory and evidence/launch9/reader-tests. The parent should record the verified
red/green result in the notebook when integrating its production changes.
