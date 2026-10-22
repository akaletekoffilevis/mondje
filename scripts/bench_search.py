#!/usr/bin/env python3
"""Measure search latency (p50/p95/p99) against a populated Mondje database.

Examples:
    python3 scripts/bench_search.py --runs 200
    python3 scripts/bench_search.py --queries electricite ciment --runs 500 --limit 50
"""

from __future__ import annotations

import argparse
import json
import statistics
import time
import urllib.parse
import urllib.request

DEFAULT_QUERIES = [
    "electricite",
    "ciment",
    "carburant",
    "livraison",
    "loyer",
    "transport",
]


def search(api: str, query: str, limit: int) -> float:
    url = f"{api}/api/Transactions?{urllib.parse.urlencode({'q': query, 'limit': limit})}"
    started = time.perf_counter()
    with urllib.request.urlopen(url, timeout=30) as response:
        response.read()
    return (time.perf_counter() - started) * 1000


def percentile(values: list[float], fraction: float) -> float:
    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, round(fraction * (len(ordered) - 1))))
    return ordered[index]


def main() -> None:
    parser = argparse.ArgumentParser(description="Measure Mondje search latency.")
    parser.add_argument("--api", default="http://localhost:5002")
    parser.add_argument("--queries", nargs="+", default=DEFAULT_QUERIES)
    parser.add_argument("--limit", type=int, default=50)
    parser.add_argument("--runs", type=int, default=200, help="total measured requests")
    parser.add_argument("--warmup", type=int, default=10)
    args = parser.parse_args()

    if args.runs < 1:
        parser.error("--runs must be positive")

    api = args.api.rstrip("/")
    for _ in range(args.warmup):
        search(api, args.queries[0], args.limit)

    samples: list[float] = []
    per_query: dict[str, list[float]] = {query: [] for query in args.queries}
    for index in range(args.runs):
        query = args.queries[index % len(args.queries)]
        elapsed = search(api, query, args.limit)
        samples.append(elapsed)
        per_query[query].append(elapsed)

    result = {
        "requests": args.runs,
        "limit": args.limit,
        "p50Ms": round(percentile(samples, 0.50), 1),
        "p95Ms": round(percentile(samples, 0.95), 1),
        "p99Ms": round(percentile(samples, 0.99), 1),
        "meanMs": round(statistics.fmean(samples), 1),
        "maxMs": round(max(samples), 1),
        "perQuery": {
            query: {
                "requests": len(values),
                "p95Ms": round(percentile(values, 0.95), 1),
            }
            for query, values in per_query.items()
            if values
        },
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
