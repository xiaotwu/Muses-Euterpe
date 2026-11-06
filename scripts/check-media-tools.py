#!/usr/bin/env python3
"""Check the external media prerequisites without contacting YouTube."""

import argparse
import os
import re
import shutil
import subprocess
import tempfile
from pathlib import Path


def executable(name, override=None, beside=None):
    filename = f"{name}.exe" if os.name == "nt" else name
    path = os.environ.get(override) if override else None
    if path:
        if not Path(path).is_absolute() or not Path(path).is_file():
            raise RuntimeError(f"{override} must point to the absolute path of the {filename} executable.")
        return str(Path(path).resolve())

    if beside and (candidate := Path(beside).with_name(filename)).is_file():
        return str(candidate)

    if path := shutil.which(filename):
        return str(Path(path).resolve())
    raise RuntimeError(f"{filename} is missing from PATH. See the media setup in INSTALL.md.")


def run(command, env=None):
    return subprocess.run(
        command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        encoding="utf-8", errors="replace", timeout=15, env=env,
    )


def checked_version(command):
    result = run(command)
    lines = result.stdout.splitlines()
    if result.returncode or not lines:
        raise RuntimeError(f"{Path(command[0]).name} did not report its version.")
    return lines[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mpv", type=Path, help="Absolute mpv path when it is outside PATH.")
    parser.add_argument("--media-dir", type=Path, help="Use the exact yt-dlp and Deno from this package directory.")
    args = parser.parse_args()
    try:
        media_dir = args.media_dir.resolve() if args.media_dir is not None else None
        mpv_path = args.mpv
        if mpv_path is None and media_dir is not None and os.name == "nt":
            mpv_path = media_dir / "mpv.exe"
        if mpv_path is not None and (not mpv_path.is_absolute() or not mpv_path.is_file()):
            raise RuntimeError("--mpv must be the absolute path of an executable.")
        mpv = str(mpv_path) if mpv_path is not None else executable("mpv")
        if media_dir is None:
            yt_dlp = executable("yt-dlp", "MUSES_YTDLP_PATH")
            deno = executable("deno", "MUSES_DENO_PATH", beside=yt_dlp)
        else:
            suffix = ".exe" if os.name == "nt" else ""
            yt_dlp, deno = (str(media_dir / (name + suffix)) for name in ("yt-dlp", "deno"))
        mpv_version = checked_version([mpv, "--no-config", "--version"]).split(maxsplit=2)[:2]

        with tempfile.TemporaryDirectory(prefix="muses-media-check-") as cache:
            env = {**os.environ, "DENO_DIR": cache, "DENO_NO_UPDATE_CHECK": "1"}
            # yt-dlp emits its dependency diagnostics before reporting the missing URL.
            result = run([
                yt_dlp, "--ignore-config", "--no-plugin-dirs", "--no-remote-components",
                "--no-cookies", "--no-cookies-from-browser", "--no-cache-dir",
                "--js-runtimes", f"deno:{deno}", "--verbose",
            ], env=env)

        version = re.search(r"(?m)^\[debug\] yt-dlp version ([^\r\n]+)", result.stdout)
        runtime = re.search(r"(?m)^\[debug\] JS runtimes: .*?\bdeno-([\w.+-]+)", result.stdout)
        ejs = re.search(r"(?m)^\[debug\] Optional libraries: .*?\byt_dlp_ejs-([\w.+-]+)", result.stdout)
        if result.returncode not in (0, 2) or not version or not runtime or not ejs:
            raise RuntimeError(
                "yt-dlp must report local yt-dlp-ejs and a supported Deno runtime. "
                "Install the upstream standalone yt-dlp and compatible Deno from INSTALL.md."
            )
        print(" ".join(mpv_version))
        print(f"yt-dlp {version[1]} with EJS {ejs[1]} and Deno {runtime[1]}")
    except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
