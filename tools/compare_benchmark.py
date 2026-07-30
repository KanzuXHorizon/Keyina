#!/usr/bin/env python3
"""Compare Keyina benchmark JSON documents and fail on latency regression."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any


def _case_map(document: dict[str, Any], label: str) -> dict[str, dict[str, Any]]:
    cases = document.get("cases")
    if not isinstance(cases, list):
        raise ValueError(f"{label}: 'cases' must be an array")

    result: dict[str, dict[str, Any]] = {}
    for index, case in enumerate(cases):
        if not isinstance(case, dict):
            raise ValueError(f"{label}: case {index} must be an object")
        name = case.get("name")
        p99 = case.get("p99_ns")
        if not isinstance(name, str) or not name:
            raise ValueError(f"{label}: case {index} has invalid name")
        if name in result:
            raise ValueError(f"{label}: duplicate benchmark case {name!r}")
        if not isinstance(p99, (int, float)) or isinstance(p99, bool) or p99 < 0:
            raise ValueError(f"{label}: case {name!r} has invalid p99_ns")
        result[name] = case
    return result


def _validate_v2_case(case: dict[str, Any], label: str, name: str) -> None:
    allocations = case.get("allocations_per_operation")
    budget = case.get("allocation_budget")
    budget_pass = case.get("budget_pass")
    if (
        not isinstance(allocations, (int, float))
        or isinstance(allocations, bool)
        or allocations < 0
    ):
        raise ValueError(
            f"{label}: case {name!r} has invalid allocations_per_operation"
        )
    if not isinstance(budget, (int, float)) or isinstance(budget, bool) or budget < 0:
        raise ValueError(f"{label}: case {name!r} has invalid allocation_budget")
    if not isinstance(budget_pass, bool):
        raise ValueError(f"{label}: case {name!r} has invalid budget_pass")
    if budget_pass != (float(allocations) <= float(budget)):
        raise ValueError(f"{label}: case {name!r} has inconsistent budget_pass")


def compare_documents(
    baseline: dict[str, Any],
    current: dict[str, Any],
    threshold: float = 0.20,
) -> list[str]:
    if threshold < 0:
        raise ValueError("threshold must be non-negative")

    baseline_schema = baseline.get("schema_version")
    current_schema = current.get("schema_version")
    if baseline_schema != current_schema:
        return [
            "schema_version mismatch: "
            f"baseline={baseline_schema} current={current_schema}"
        ]
    if baseline_schema not in {1, 2}:
        return [f"unsupported schema_version: {baseline_schema}"]

    baseline_cases = _case_map(baseline, "baseline")
    current_cases = _case_map(current, "current")
    errors: list[str] = []

    if current_schema == 2:
        for name, case in baseline_cases.items():
            _validate_v2_case(case, "baseline", name)
        for name, case in current_cases.items():
            _validate_v2_case(case, "current", name)
            if not case["budget_pass"]:
                errors.append(
                    f"{name}: allocation budget failed "
                    f"({float(case['allocations_per_operation']):.2f} > "
                    f"{float(case['allocation_budget']):.2f})"
                )

    for name, baseline_case in baseline_cases.items():
        current_case = current_cases.get(name)
        if current_case is None:
            errors.append(f"current result is missing benchmark case {name!r}")
            continue
        baseline_p99 = float(baseline_case["p99_ns"])
        current_p99 = float(current_case["p99_ns"])
        allowed = baseline_p99 * (1.0 + threshold)
        if current_p99 > allowed:
            regression = (
                float("inf")
                if baseline_p99 == 0
                else (current_p99 / baseline_p99 - 1.0) * 100.0
            )
            errors.append(
                f"{name}: p99 regressed {regression:.2f}% "
                f"({baseline_p99:.2f} ns -> {current_p99:.2f} ns), "
                f"limit={threshold * 100:.2f}%"
            )
    return errors


def _load(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"cannot read benchmark document {path}: {exc}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"{path}: top-level JSON value must be an object")
    return value


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline", type=Path)
    parser.add_argument("current", type=Path)
    parser.add_argument("--threshold", type=float, default=0.20)
    args = parser.parse_args()

    try:
        errors = compare_documents(
            _load(args.baseline), _load(args.current), args.threshold
        )
    except ValueError as exc:
        parser.error(str(exc))

    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1
    print(
        f"benchmark comparison passed at {args.threshold * 100:.2f}% threshold"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
