from __future__ import annotations

import unittest

from compare_benchmark import compare_documents


def document(*cases: tuple[str, float], schema_version: int = 1) -> dict:
    return {
        "schema_version": schema_version,
        "environment": {"compiler": "test"},
        "cases": [
            {
                "name": name,
                "median_ns": p99 / 2,
                "p95_ns": p99 * 0.8,
                "p99_ns": p99,
                "max_ns": p99 * 1.2,
            }
            for name, p99 in cases
        ],
    }


class CompareBenchmarkTest(unittest.TestCase):
    def test_accepts_regression_at_or_below_threshold(self) -> None:
        baseline = document(("ascii", 100.0), ("tone", 200.0))
        current = document(("ascii", 120.0), ("tone", 180.0))
        self.assertEqual(compare_documents(baseline, current, 0.20), [])

    def test_rejects_regression_above_threshold(self) -> None:
        baseline = document(("ascii", 100.0))
        current = document(("ascii", 120.1))
        errors = compare_documents(baseline, current, 0.20)
        self.assertEqual(len(errors), 1)
        self.assertIn("ascii", errors[0])

    def test_rejects_missing_case(self) -> None:
        baseline = document(("ascii", 100.0), ("tone", 200.0))
        current = document(("ascii", 90.0))
        self.assertEqual(
            compare_documents(baseline, current, 0.20),
            ["current result is missing benchmark case 'tone'"],
        )

    def test_rejects_incompatible_schema(self) -> None:
        baseline = document(("ascii", 100.0), schema_version=1)
        current = document(("ascii", 90.0), schema_version=2)
        self.assertEqual(
            compare_documents(baseline, current, 0.20),
            ["schema_version mismatch: baseline=1 current=2"],
        )


if __name__ == "__main__":
    unittest.main()
