#!/usr/bin/env python3
"""Check the Windows MSIX metadata and write its checksum and planned download URL."""

import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


REPOSITORY = "https://github.com/xiaotwu/Muses-Euterpe"
MSIX_NAMESPACE = "{http://schemas.microsoft.com/appx/manifest/foundation/windows10}"
VERSION = re.compile(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)")


def inspect_msix(path, version, publisher, allow_unsigned):
    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        manifest = ET.fromstring(archive.read("AppxManifest.xml"))
        identity = manifest.find(f"{MSIX_NAMESPACE}Identity")
        expected = {
            "Name": "xiaotwu.Muses",
            "Publisher": publisher,
            "Version": f"{version}.0",
            "ProcessorArchitecture": "x64",
        }
        if identity is None or any(identity.get(key) != value for key, value in expected.items()):
            raise ValueError(f"{path.name} has an unexpected MSIX identity, publisher, version or architecture.")
        required = {"Muses.exe", "MediaTools/SOURCES.json", "MediaTools/mpv.exe",
                    "MediaTools/yt-dlp.exe", "MediaTools/deno.exe"}
        if not required <= names:
            raise ValueError(f"{path.name} is missing required files: {', '.join(sorted(required - names))}.")
        signature_present = "AppxSignature.p7x" in names
        if not signature_present and not allow_unsigned:
            raise ValueError(f"{path.name} is unsigned. Use --allow-unsigned-msix only for CI rehearsals.")
        return {"publisher": publisher, "signature_present": signature_present}


def record_asset(path, version, publisher, allow_unsigned):
    expected = f"Muses-{version}-win-x64.msix"
    if path.name != expected:
        raise ValueError(f"Expected {expected}, received {path.name}.")
    metadata = inspect_msix(path, version, publisher, allow_unsigned)
    with path.open("rb") as content:
        checksum = hashlib.file_digest(content, "sha256").hexdigest()
    return {
        "name": expected,
        "runtime": "win-x64",
        "size": path.stat().st_size,
        "digest": f"sha256:{checksum}",
        "browser_download_url": f"{REPOSITORY}/releases/download/v{version}/{expected}",
        **metadata,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version", help="Stable X.Y.Z package version")
    parser.add_argument("--windows", type=Path, required=True, help="Path to the x64 MSIX")
    parser.add_argument("--publisher", default="CN=xiaotwu", help="Exact expected MSIX Publisher")
    parser.add_argument("--allow-unsigned-msix", action="store_true", help="Allow an unsigned CI inspection package")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    version = args.version
    if not VERSION.fullmatch(version) or not all(0 <= int(part) <= 65535 for part in version.split(".")) or version == "0.0.0":
        parser.error("use a nonzero stable X.Y.Z version, with components from 0 through 65535")

    try:
        asset = record_asset(args.windows, version, args.publisher, args.allow_unsigned_msix)
        args.output.mkdir(parents=True, exist_ok=True)
        (args.output / "SHA256SUMS").write_text(
            f"{asset['digest'][7:]}  {asset['name']}\n", encoding="utf-8", newline="\n",
        )
        plan = {"tag": f"v{version}", "status": "planned", "assets": [asset]}
        (args.output / "release-asset-plan.json").write_text(
            json.dumps(plan, indent=2) + "\n", encoding="utf-8", newline="\n",
        )
        print(f"Verified {asset['name']} ({asset['digest']}).")
    except (OSError, ValueError, KeyError, ET.ParseError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Release asset check failed: {error}\n")


if __name__ == "__main__":
    main()
