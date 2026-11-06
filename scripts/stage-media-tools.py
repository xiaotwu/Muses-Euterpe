#!/usr/bin/env python3
"""Stage the pinned media tools and their notices into a package payload."""

import argparse
import hashlib
import json
import os
import shutil
import tempfile
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path


LOCK = Path(__file__).with_name("media-tools.lock.json")
PLATFORMS = {
    "win-x64": ("windows-mpv", "windows-yt-dlp", "windows-deno"),
    "linux-x64": ("linux-yt-dlp", "linux-deno"),
}
NOTICES = ("yt-dlp-license", "yt-dlp-binary-license", "deno-license")


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def verified_asset(asset, cache):
    url, expected = asset["url"], asset["sha256"]
    path = cache / f"{expected[:16]}-{url.rsplit('/', 1)[-1]}"
    if path.is_file() and digest(path) == expected:
        return path

    print(f"Downloading {url}", flush=True)
    with tempfile.TemporaryDirectory(prefix="download-", dir=cache) as temporary:
        incoming = Path(temporary) / "asset"
        request = urllib.request.Request(url, headers={"User-Agent": "Muses-Euterpe-packaging"})
        for attempt in range(3):
            try:
                with urllib.request.urlopen(request, timeout=45) as response, incoming.open("wb") as output:
                    shutil.copyfileobj(response, output, 1024 * 1024)
                break
            except (OSError, urllib.error.URLError):
                if attempt == 2:
                    raise
                time.sleep(attempt + 1)
        if digest(incoming) != expected:
            raise ValueError(f"SHA-256 mismatch for {url}")
        os.replace(incoming, path)
    return path


def stage_asset(asset, cache, destination):
    path = verified_asset(asset, cache)
    if "files" in asset:
        with zipfile.ZipFile(path) as archive:
            for filename, member in asset["files"].items():
                output = destination / filename
                output.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(member) as source, output.open("wb") as target:
                    shutil.copyfileobj(source, target, 1024 * 1024)
    else:
        output = destination / asset["file"]
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path, output)


def stage(runtime, destination, cache):
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    assets = {key: lock[key] for key in (*PLATFORMS[runtime], *NOTICES)}
    cache.mkdir(parents=True, exist_ok=True)
    destination.mkdir(parents=True, exist_ok=True)
    for asset in assets.values():
        stage_asset(asset, cache, destination)
    if runtime == "linux-x64":
        for name in ("yt-dlp", "deno"):
            (destination / name).chmod(0o755)
    (destination / "SOURCES.json").write_text(json.dumps(assets, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("runtime", choices=PLATFORMS)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    cache_path = os.environ.get("MUSES_MEDIA_BUILD_CACHE")
    try:
        if cache_path:
            cache = Path(cache_path)
            if not cache.is_absolute():
                raise ValueError("MUSES_MEDIA_BUILD_CACHE must be an absolute path")
            stage(args.runtime, args.destination, cache)
        else:
            with tempfile.TemporaryDirectory(prefix="muses-media-build-") as temporary:
                stage(args.runtime, args.destination, Path(temporary))
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Could not stage media tools: {error}\n")


if __name__ == "__main__":
    main()
