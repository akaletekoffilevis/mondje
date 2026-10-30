#!/usr/bin/env python3
"""Check that the full export total equals the balance endpoint (challenge 14).

The export is streamed line by line, so this works on large databases without loading
the whole file in memory. Use --include-deleted only if you want to compare against a
different row set than the default (the balance excludes deleted rows).

Example:
    python3 scripts/check_export_balance.py
    python3 scripts/check_export_balance.py --at 2026-10-19T12:00:00Z
"""

from __future__ import annotations

import argparse
import json
import urllib.parse
import urllib.request


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify export total == balance.")
    parser.add_argument("--api", default="http://localhost:5001")
    parser.add_argument("--at", help="ISO 8601 balance cutoff")
    parser.add_argument("--query", default="", help="optional search word filter")
    parser.add_argument("--include-deleted", action="store_true")
    args = parser.parse_args()

    api = args.api.rstrip("/")
    export_params = {"format": "json"}
    if args.query:
        export_params["q"] = args.query
    if args.include_deleted:
        export_params["includeDeleted"] = "true"

    credit = 0
    debit = 0
    count = 0
    with urllib.request.urlopen(
        f"{api}/api/Transactions/export?{urllib.parse.urlencode(export_params)}", timeout=600
    ) as response:
        for raw in response:
            line = raw.strip()
            if not line:
                continue
            row = json.loads(line)
            count += 1
            if row["type"] == "CREDIT":
                credit += row["montant"]
            else:
                debit += row["montant"]

    balance_params = {}
    if args.at:
        balance_params["at"] = args.at
    balance_url = f"{api}/api/Transactions/balance"
    if balance_params:
        balance_url += f"?{urllib.parse.urlencode(balance_params)}"
    with urllib.request.urlopen(balance_url, timeout=60) as response:
        balance = json.load(response)

    exported = credit - debit
    matches = exported == balance["balance"] and count == balance["transactionCount"]
    result = {
        "export": {
            "transactions": count,
            "credit": credit,
            "debit": debit,
            "balance": exported,
        },
        "balanceEndpoint": {
            "transactions": balance["transactionCount"],
            "credit": balance["credit"],
            "debit": balance["debit"],
            "balance": balance["balance"],
            "currency": balance["currency"],
        },
        "matches": matches,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(0 if matches else 1)


if __name__ == "__main__":
    main()
