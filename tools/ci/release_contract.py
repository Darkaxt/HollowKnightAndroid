#!/usr/bin/env python3
"""Fail-closed release artifact and package selection for GitHub Actions."""

from __future__ import annotations

import argparse
import pathlib
import re


PRODUCTION_PACKAGE = "io.github.darkaxt.dualsouls"
VERSION_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$")


def select_release_apk(build_directory: pathlib.Path, version: str) -> pathlib.Path:
    if not VERSION_PATTERN.fullmatch(version):
        raise ValueError(f"release version is invalid: {version!r}")

    build_directory = pathlib.Path(build_directory)
    expected = build_directory / f"DualSouls-{version}.apk"
    apks = sorted(path for path in build_directory.glob("*.apk") if path.is_file())
    unexpected = [path for path in apks if path.name != expected.name]
    if unexpected:
        names = ", ".join(path.name for path in unexpected)
        raise ValueError(f"unexpected APK in release build directory: {names}")
    if not expected.is_file():
        raise ValueError(f"expected release APK is missing: {expected.name}")
    return expected


def require_production_package(package_name: str) -> None:
    if package_name != PRODUCTION_PACKAGE:
        raise ValueError(
            f"release APK is not the production package: {package_name!r}; "
            f"expected {PRODUCTION_PACKAGE!r}",
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)

    select = commands.add_parser("select-apk")
    select.add_argument("--build-dir", required=True, type=pathlib.Path)
    select.add_argument("--version", required=True)

    package = commands.add_parser("check-package")
    package.add_argument("--package", required=True)

    args = parser.parse_args()
    try:
        if args.command == "select-apk":
            print(select_release_apk(args.build_dir, args.version))
        else:
            require_production_package(args.package)
    except ValueError as error:
        parser.error(str(error))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
