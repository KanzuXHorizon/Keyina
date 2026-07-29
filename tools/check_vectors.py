#!/usr/bin/env python3
"""Validate Keyina golden-vector files without building the C++ project."""

from __future__ import annotations

import argparse
from pathlib import Path

ALLOWED_GUARD_REASONS = {
    "None",
    "Url",
    "Email",
    "FilePath",
    "Identifier",
    "VersionOrHash",
    "ShellToken",
}


def validate(path: Path) -> int:
    seen_raw: set[str] = set()
    count = 0

    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exc:
        raise ValueError(f"cannot read {path}: {exc}") from exc

    for line_number, line in enumerate(lines, start=1):
        if not line or line.startswith("#"):
            continue
        fields = line.split("\t")
        if len(fields) != 4:
            raise ValueError(
                f"{path}:{line_number}: expected 4 tab-separated columns, "
                f"found {len(fields)}"
            )
        raw, expected, rollback, guard_reason = fields
        if not raw or not expected or not rollback:
            raise ValueError(f"{path}:{line_number}: text columns must be non-empty")
        if guard_reason not in ALLOWED_GUARD_REASONS:
            raise ValueError(
                f"{path}:{line_number}: unsupported guard reason {guard_reason!r}"
            )
        if raw in seen_raw:
            raise ValueError(f"{path}:{line_number}: duplicate raw sequence {raw!r}")
        if rollback != raw:
            raise ValueError(
                f"{path}:{line_number}: rollback must preserve exact raw keys"
            )
        seen_raw.add(raw)
        count += 1

    if count < 100:
        raise ValueError(f"{path}: expected at least 100 vectors, found {count}")
    return count


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "path",
        nargs="?",
        type=Path,
        default=Path("tests/data/telex_vectors.tsv"),
    )
    args = parser.parse_args()

    try:
        count = validate(args.path)
    except ValueError as exc:
        parser.error(str(exc))
    print(f"validated {count} golden vectors from {args.path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
