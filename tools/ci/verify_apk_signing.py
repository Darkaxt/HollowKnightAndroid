#!/usr/bin/env python3
"""Verify an APK and require its signer certificate to match a public pin."""

from __future__ import annotations

import argparse
import os
import pathlib
import re
import subprocess
import sys


CERTIFICATE_LINE = re.compile(
    r"certificate SHA-256 digest:\s*([0-9A-Fa-f:\s]+)", re.IGNORECASE
)


def normalize_certificate_sha256(value: str) -> str:
    compact = re.sub(r"[:\s]", "", value).lower()
    if not re.fullmatch(r"[0-9a-f]{64}", compact):
        raise ValueError("certificate SHA-256 must contain exactly 64 hexadecimal digits")
    return compact


def extract_certificate_sha256(apksigner_output: str) -> str:
    match = CERTIFICATE_LINE.search(apksigner_output)
    if match is None:
        raise ValueError("apksigner did not report a signer certificate SHA-256 digest")
    return normalize_certificate_sha256(match.group(1))


def _version_key(path: pathlib.Path) -> tuple[tuple[int, object], ...]:
    parts = re.findall(r"\d+|[^\d]+", path.name)
    return tuple((0, int(part)) if part.isdigit() else (1, part) for part in parts)


def find_apksigner() -> pathlib.Path:
    explicit = os.environ.get("APKSIGNER")
    if explicit:
        candidate = pathlib.Path(explicit)
        if candidate.is_file():
            return candidate
        raise FileNotFoundError(f"APKSIGNER does not exist: {candidate}")

    for variable in ("ANDROID_HOME", "ANDROID_SDK_ROOT"):
        sdk = os.environ.get(variable)
        if not sdk:
            continue
        build_tools = pathlib.Path(sdk) / "build-tools"
        if not build_tools.is_dir():
            continue
        for version in sorted(build_tools.iterdir(), key=_version_key, reverse=True):
            for name in ("apksigner", "apksigner.bat"):
                candidate = version / name
                if candidate.is_file():
                    return candidate
    raise FileNotFoundError(
        "apksigner was not found; set APKSIGNER, ANDROID_HOME, or ANDROID_SDK_ROOT"
    )


def verify_apk(apk: pathlib.Path, expected_certificate_sha256: str) -> str:
    if not apk.is_file():
        raise FileNotFoundError(f"APK does not exist: {apk}")
    expected = normalize_certificate_sha256(expected_certificate_sha256)
    command = [str(find_apksigner()), "verify", "--verbose", "--print-certs", str(apk)]
    result = subprocess.run(command, capture_output=True, text=True, check=False)
    output = "\n".join(part for part in (result.stdout, result.stderr) if part)
    if result.returncode != 0:
        raise RuntimeError(f"apksigner rejected {apk}:\n{output.strip()}")
    actual = extract_certificate_sha256(output)
    if actual != expected:
        raise RuntimeError(
            f"unexpected APK signer certificate SHA-256: {actual}; expected {expected}"
        )
    return actual


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--apk", required=True, type=pathlib.Path)
    parser.add_argument("--expected-certificate-sha256", required=True)
    args = parser.parse_args()
    try:
        actual = verify_apk(args.apk, args.expected_certificate_sha256)
    except (FileNotFoundError, RuntimeError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(f"APK signer certificate SHA-256: {actual}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
