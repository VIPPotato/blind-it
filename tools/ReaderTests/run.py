"""Offline, native-Windows build/run and evidence receipts for ReaderTests.

No packages, downloads, game process, screen reader, git, or Kanban calls.
Artifacts stay in tools/ReaderTests and evidence/launch9/reader-tests.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
EVIDENCE = ROOT / "evidence/launch9/reader-tests"
PROJECT = HERE / "ReaderTests.csproj"
DOTNET = Path("C:/Program Files/dotnet/dotnet.exe")
PACKS = "C:/program files/dotnet/packs"
BASELINE = ROOT / "evidence/launch9/baseline/src/BlindIt"
BASELINE_READER_SHA256 = "439da166d42b2a21c5b24cf972f8318d9e36719429ed68818be37c37f2878ffd"
EXPECTED_BASELINE_FAILURES = {
    "calibration_stage_cycle_one_automatic_each",
    "calibration_context_change_does_not_poll_duplicate",
    "calibration_entry_has_no_spoken_positions",
    "calibration_repeat_has_no_spoken_positions",
    "calibration_review_has_no_spoken_positions",
    "leaderboard_browse_marks_rank_gap_both_directions",
    "leaderboard_rank_snapshot_refreshes_with_text",
    "leaderboard_late_fill_queues_behind_entry",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compiled_sources(mod_src: Path, reader: Path) -> dict[str, Path]:
    """Read the project's literal Compile list, not a parallel source inventory."""
    sources = {}
    for item in ET.parse(PROJECT).getroot().iter("Compile"):
        original = item.attrib["Include"]
        expanded = original.replace("$(ReaderSource)", reader.as_posix()).replace("$(ModSrc)", mod_src.as_posix())
        if "$(" in expanded:
            raise ValueError("Unrecognized Compile property; update receipt resolver: " + original)
        path = Path(expanded)
        if not path.is_absolute():
            path = HERE / path
        path = path.resolve()
        if path.is_file():
            sources[path.name] = path
        elif path.name not in {"LeaderboardWindow.cs"}:
            raise FileNotFoundError(path)
    return sources


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("variant", choices=("baseline", "current"))
    parser.add_argument("--reader-source", type=Path, help="Override only ScreenReader.cs; helpers still come from the selected ModSrc.")
    parser.add_argument("--expect-red", action="store_true", help="Accept only the expected baseline assertion failures, never build/runtime errors.")
    args = parser.parse_args()
    if args.expect_red and args.variant != "baseline":
        parser.error("--expect-red is only valid for the immutable baseline")
    if not DOTNET.is_file():
        parser.error("Native Windows dotnet is missing: " + str(DOTNET))
    if not Path(PACKS).is_dir():
        parser.error("Installed pack source is missing: " + PACKS)

    mod_src = BASELINE if args.variant == "baseline" else ROOT / "src/BlindIt"
    reader = (args.reader_source or mod_src / "ScreenReader.cs").resolve()
    if args.variant == "baseline":
        if reader != (BASELINE / "ScreenReader.cs").resolve() or sha256(reader) != BASELINE_READER_SHA256:
            parser.error("Baseline reader path/bytes changed; refusing a counterfeit pre-fix control")

    evidence = EVIDENCE / args.variant
    evidence.mkdir(parents=True, exist_ok=True)
    result_path = evidence / "results.json"
    dll = HERE / "bin" / args.variant / "Release/net8.0/ReaderTests.dll"
    properties = ["-p:ReaderVariant=" + args.variant, "-p:ModSrc=" + mod_src.as_posix()]
    if args.reader_source:
        properties.append("-p:ReaderSource=" + reader.as_posix())
    commands = [
        [str(DOTNET), "restore", str(PROJECT), "--source", PACKS, "--ignore-failed-sources", "-p:NuGetAudit=false", *properties],
        [str(DOTNET), "build", str(PROJECT), "--configuration", "Release", "--no-restore", "--no-incremental",
         "-p:UseSharedCompilation=false", "-nodeReuse:false", *properties],
        [str(DOTNET), str(dll), "--json", str(result_path)],
    ]
    env = os.environ.copy()
    env.update({
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "1",
        "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_HOME": str(HERE / ".dotnet-home"),
        "NUGET_HTTP_CACHE_PATH": str(HERE / ".nuget-http-cache"),
        "PYTHONDONTWRITEBYTECODE": "1",
    })
    display_commands = [shlex.join(command) for command in commands]
    (evidence / "commands.txt").write_text(
        "# Working directory: " + ROOT.as_posix() + "\n"
        "# Native Windows dotnet; Bash-compatible quoting. Restore has only a local source.\n"
        + "\n".join(display_commands) + "\n", encoding="utf-8")

    sources = compiled_sources(mod_src, reader)
    before = {name: {"Path": path.as_posix(), "SHA256": sha256(path)} for name, path in sources.items()}
    manifest = {
        "Variant": args.variant,
        "ExpectedRed": args.expect_red,
        "WorkingDirectory": ROOT.as_posix(),
        "Commands": commands,
        "ProcessEnvironmentOverrides": {k: env[k] for k in (
            "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE",
            "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE", "DOTNET_NOLOGO", "DOTNET_CLI_HOME",
            "NUGET_HTTP_CACHE_PATH", "PYTHONDONTWRITEBYTECODE")},
        "ProjectSHA256": sha256(PROJECT),
        "RunnerSHA256": sha256(Path(__file__)),
        "InputsBefore": before,
        "RestoreExitCode": None, "BuildExitCode": None, "TestExitCode": None,
    }

    def save_manifest() -> None:
        (evidence / "run-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    def run(index: int, log_name: str) -> int:
        print("COMMAND: " + display_commands[index], flush=True)
        completed = subprocess.run(commands[index], cwd=ROOT, env=env, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace", timeout=180)
        (evidence / log_name).write_text(completed.stdout, encoding="utf-8")
        print(completed.stdout, end="" if completed.stdout.endswith("\n") else "\n", flush=True)
        print("PROCESS_EXIT: " + str(completed.returncode), flush=True)
        return completed.returncode

    try:
        manifest["RestoreExitCode"] = run(0, "restore.log")
        save_manifest()
        if manifest["RestoreExitCode"] != 0:
            print("BLOCKED: offline restore failed; no behavioral red result accepted.")
            return 2
        manifest["BuildExitCode"] = run(1, "build.log")
        save_manifest()
        if manifest["BuildExitCode"] != 0:
            print("BLOCKED: harness did not compile; no behavioral red result accepted.")
            return 2
        after = {name: sha256(path) for name, path in sources.items()}
        manifest["InputsAfterBuild"] = after
        if after != {name: value["SHA256"] for name, value in before.items()}:
            manifest["Validation"] = "REJECTED: source changed during restore/build; rerun after edits settle"
            save_manifest()
            print(manifest["Validation"])
            return 2
        manifest["AssemblySHA256"] = sha256(dll)
        manifest["TestExitCode"] = run(2, "tests.log")
        save_manifest()
        report = json.loads(result_path.read_text(encoding="utf-8"))
        tests = report["Tests"]
        status_counts = {status: sum(test["Status"] == status for test in tests) for status in ("pass", "fail", "error")}
        if (report["Total"] != len(tests) or report["Passed"] != status_counts["pass"]
                or report["Failed"] != status_counts["fail"] or report["Errors"] != status_counts["error"]
                or report["ExitCode"] != manifest["TestExitCode"]):
            raise ValueError("Result counters or process exit disagree with enumerated test results")
        production_hashes = {name: value["SHA256"] for name, value in before.items()
                             if sources[name].parent != HERE}
        if report["Sources"] != production_hashes:
            raise ValueError("Executable's embedded source hashes differ from build inputs")
        manifest["VerifiedReport"] = {key: report[key] for key in ("Passed", "Failed", "Errors", "Total", "ExitCode")}
        failed = {test["Name"] for test in tests if test["Status"] == "fail"}
        if args.expect_red:
            if manifest["TestExitCode"] != 1 or report["Errors"] != 0 or failed != EXPECTED_BASELINE_FAILURES:
                raise ValueError("Baseline must fail exactly the known behavioral regressions; got " + repr(sorted(failed)))
            manifest["Validation"] = ("EXPECTED-RED VERIFIED: build succeeded; "
                                      + str(report["Failed"]) + " expected behavioral failures; controls passed")
            exit_code = 0
        else:
            manifest["Validation"] = "PASS" if manifest["TestExitCode"] == 0 else "TESTS FAILED (see results.json)"
            exit_code = manifest["TestExitCode"]
        save_manifest()
        print(manifest["Validation"])
        print("EVIDENCE: " + evidence.as_posix())
        return exit_code
    except (OSError, ValueError, KeyError, subprocess.TimeoutExpired) as exc:
        manifest["Validation"] = "HARNESS/RECEIPT ERROR: " + str(exc)
        save_manifest()
        print(manifest["Validation"], file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
