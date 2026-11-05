"""Run every check a release needs, and fail if any of them fails or couldn't run.

    python tools/check_release.py --game 1.3.40.10 "<V3.4>/Data/Config" --game 1.3.30.17 "<V3.3>/Data/Config"

Each --game is a version and that game's Data/Config: V3.3 b17 is 1.3.30.17 and V3.4 b10
is 1.3.40.10 (see check_patches.py). The version isn't read from the files, so give the
right one: V3.4's files labelled as V3.3 still pass. Every game line in LINES has to be
there, so a release can't skip one by accident.

    Public names   check_public_names.py
    Patches        check_patches.py on each game, with every add-on loaded. A file or
                   condition it had to skip counts as a failure here
    DLLs           a change to a DLL's sources in a commit the DLL's own commit doesn't
                   have. Git can't see a DLL built before the last edit and then committed
                   with it, so build right before you commit
    Uncommitted    anything uncommitted in a mod folder or src: package.py zips the
                   folders as they are, a test build included
"""

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

from package import MODS

ROOT = Path(__file__).resolve().parent.parent

# Game lines a release has to pass on: (release type, major, minor) -> name.
LINES = {(1, 3, 30): "V3.3", (1, 3, 40): "V3.4"}

# The game's load order. Practice's patches only apply with Shugyō, so count it as loaded.
PATCH_ORDER = ["KitsuneUI", "KitsuneUI_JobPreview", "KitsuneUI_Practice", "KitsuneUI_Settings", "KitsuneUI_Toolbelt20", "KitsuneUI_Loading"]
LOADED = "KitsuneShugyo"


def run(*args):
    """The exit code and the output of a command run from the repo's root."""
    r = subprocess.run(args, cwd=ROOT, capture_output=True, encoding="utf-8", errors="replace")
    return r.returncode, r.stdout + r.stderr


def git(*args):
    code, out = run("git", *args)
    if code:
        sys.exit(f"git {' '.join(args)} failed:\n{out}")
    return out.strip()


def report(name, problems, ok_note=""):
    """Prints one check's result and returns whether it passed."""
    if not problems:
        print(f"ok    {name}{': ' + ok_note if ok_note else ''}")
        return True
    print(f"FAIL  {name}")
    for p in problems:
        print(f"      {p}")
    return False


def check_names():
    code, out = run(sys.executable, "tools/check_public_names.py")
    lines = out.strip().splitlines()
    return report("Public names", lines if code else [], lines[-1] if lines else "")


def check_patches(version, config):
    name = f"Patches on {LINES[version_tuple(version)[:3]]} ({version})"
    if not (Path(config) / "XUi_InGame" / "windows.xml").is_file():
        return report(name, [f"{config} isn't a game's Data/Config"])
    code, out = run(sys.executable, "tools/check_patches.py", config, *(f"{m}/Config" for m in PATCH_ORDER),
                    "--loaded", LOADED, "--game-version", version)
    lines = out.strip().splitlines()
    problems = [l.strip() for l in lines if "skipped" in l or l.startswith("  ")]
    if code and not problems:
        problems = lines[-5:]
    applied = sum(int(m.group(1)) for m in re.finditer(r": (\d+) applied", out))
    return report(name, problems, f"{applied} applied, 0 missed")


def dll_sources(csproj):
    """The DLL's path in the repo, and the paths it's built from: the project's own
    folder and any file it links from outside it."""
    text = csproj.read_text(encoding="utf-8")
    name = re.search(r"<AssemblyName>([^<]+)</AssemblyName>", text).group(1)
    sources = {csproj.parent.relative_to(ROOT).as_posix()}
    for inc in re.findall(r'<Compile Include="([^"]+)"', text):
        path = os.path.normpath(csproj.parent / inc.replace("\\", "/"))
        sources.add(Path(path).relative_to(ROOT).as_posix())
    return f"{name}/{name}.dll", sorted(sources)


def check_dlls():
    problems = []
    projects = sorted((ROOT / "src").glob("*/*.csproj"))
    for csproj in projects:
        dll, sources = dll_sources(csproj)
        built = git("log", "-1", "--format=%H", "--", dll)
        if not built:
            problems.append(f"{dll} isn't committed")
            continue
        newer = git("log", "--format=%h %s", f"{built}..HEAD", "--", *sources)
        if newer:
            problems.append(f"{dll} is older than its source, rebuild it:")
            problems += [f"  {line}" for line in newer.splitlines()]
    return report("DLLs", problems, f"all {len(projects)} are up to date with their source")


def check_uncommitted():
    out = git("status", "--porcelain", "--", *MODS, "src")
    return report("Uncommitted", out.splitlines(), "none in the mod folders or src")


def version_tuple(text):
    return tuple(int(p) for p in text.split("."))


def main():
    # A commit subject can have a character the Windows console can't print.
    sys.stdout.reconfigure(errors="replace")
    parser = argparse.ArgumentParser(description="Run every check a release needs.")
    parser.add_argument("--game", nargs=2, action="append", required=True, metavar=("VERSION", "CONFIG"),
                        help="a game version (1.3.40.10) and that game's Data/Config")
    args = parser.parse_args()

    try:
        given = {version_tuple(v)[:3] for v, _ in args.game}
    except ValueError:
        sys.exit("A game version is four numbers, like 1.3.40.10")
    unknown = given - LINES.keys()
    if unknown:
        sys.exit(f"Not a game line this checks: {sorted(unknown)}. Add it to LINES if it's new.")
    missing = LINES.keys() - given
    if missing:
        sys.exit("Missing --game for " + ", ".join(LINES[m] for m in sorted(missing)))

    results = [check_names()]
    results += [check_patches(v, c) for v, c in args.game]
    results += [check_dlls(), check_uncommitted()]
    if not all(results):
        print(f"\n{results.count(False)} of {len(results)} checks failed. Don't release yet.")
        sys.exit(1)
    print(f"\nAll {len(results)} checks passed.")


if __name__ == "__main__":
    main()
