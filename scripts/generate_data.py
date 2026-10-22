#!/usr/bin/env python3
"""Generate representative Mondje import files.

Examples:
    python3 scripts/generate_data.py --lines 100000 --output /tmp/mondje-100k.txt
    python3 scripts/generate_data.py --lines 1000000 --format ndjson --broken 0.02 \
        --output /tmp/mondje-1m.ndjson

The UUIDs are deterministic, so two runs with the same arguments produce the same
file (useful to reproduce a benchmark). Broken lines are spread through the file
and cover the same categories as the automated tests.
"""

from __future__ import annotations

import argparse
import json
import random
from pathlib import Path

AGENCIES = ["Douala", "Yaoundé", "Bafoussam"]
DESCRIPTIONS = [
    "Vente au marché central",
    "Règlement facture électricité",
    "Achat carburant véhicule",
    "Livraison marchandises Douala",
    "Frais de transfert bancaire",
    "Paiement loyer boutique",
    "Encaissement client Yaoundé",
    "Achat ciment et matériaux",
    "Commission agence Bafoussam",
    "Remboursement avance fournisseur",
]
BROKEN_KINDS = [
    "bad_uuid",
    "bad_type",
    "bad_amount",
    "empty_amount",
    "too_long_description",
    "invalid_json",
]


def uuid_for(index: int) -> str:
    return f"{index:08x}-0000-4000-8000-{index:012x}"


def broken_kind_for(index: int) -> str:
    return BROKEN_KINDS[index % len(BROKEN_KINDS)]


def txt_line(index: int, rng: random.Random, broken_kind: str | None) -> str:
    transaction_id = uuid_for(index)
    transaction_type = "CREDIT" if index % 3 else "DEBIT"
    amount = 1_000 + (index * 37) % 500_000
    description = DESCRIPTIONS[index % len(DESCRIPTIONS)]

    if broken_kind == "bad_uuid":
        transaction_id = f"pas-un-uuid-{index}"
    elif broken_kind == "bad_type":
        transaction_type = "VIREMENT"
    elif broken_kind == "bad_amount":
        amount = "15.5"
    elif broken_kind == "empty_amount":
        amount = ""
    elif broken_kind == "too_long_description":
        description = "x" * 256

    return f"{transaction_id};{transaction_type};{amount};{description}"


def ndjson_line(index: int, rng: random.Random, broken_kind: str | None) -> str:
    if broken_kind == "invalid_json":
        return '{"id": "casse", "type": '

    payload = {
        "id": uuid_for(index),
        "type": "CREDIT" if index % 3 else "DEBIT",
        "montant": 1_000 + (index * 37) % 500_000,
        "description": DESCRIPTIONS[index % len(DESCRIPTIONS)],
        "agency": AGENCIES[index % len(AGENCIES)],
    }

    if broken_kind == "bad_uuid":
        payload["id"] = f"pas-un-uuid-{index}"
    elif broken_kind == "bad_type":
        payload["type"] = "VIREMENT"
    elif broken_kind == "bad_amount":
        payload["montant"] = 15.5
    elif broken_kind == "empty_amount":
        payload.pop("montant")
    elif broken_kind == "too_long_description":
        payload["description"] = "x" * 256

    return json.dumps(payload, ensure_ascii=False)


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate a Mondje import file.")
    parser.add_argument("--lines", type=int, default=100_000, help="number of lines")
    parser.add_argument("--format", choices=["txt", "ndjson"], default="txt")
    parser.add_argument(
        "--broken",
        type=float,
        default=0.0,
        help="fraction of broken lines, e.g. 0.02 for 2%%",
    )
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    if args.lines < 1:
        parser.error("--lines must be positive")
    if not 0.0 <= args.broken < 1.0:
        parser.error("--broken must be between 0 and 1")

    rng = random.Random(args.seed)
    broken_total = int(args.lines * args.broken)
    broken_indexes = set(rng.sample(range(1, args.lines + 1), broken_total))

    args.output.parent.mkdir(parents=True, exist_ok=True)
    builder = txt_line if args.format == "txt" else ndjson_line

    with args.output.open("w", encoding="utf-8", newline="\n") as handle:
        for index in range(1, args.lines + 1):
            kind = broken_kind_for(index) if index in broken_indexes else None
            handle.write(builder(index, rng, kind))
            handle.write("\n")

    print(
        json.dumps(
            {
                "output": str(args.output),
                "format": args.format,
                "lines": args.lines,
                "brokenLines": broken_total,
                "sizeBytes": args.output.stat().st_size,
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
