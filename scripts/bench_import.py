#!/usr/bin/env python3
"""Upload an import file and measure how long the backend takes to process it.

The elapsed time is measured from the upload response to the terminal status, so it
includes the queue wait. The batch itself also reports `durationMs`, which is the time
spent by the import engine; both values are printed.

Example:
    python3 scripts/bench_import.py --file /tmp/mondje-100k.txt --agency Douala
"""

from __future__ import annotations

import argparse
import json
import mimetypes
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path


def request_json(url: str, method: str = "GET") -> dict:
    request = urllib.request.Request(url, method=method)
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.load(response)


def upload(url: str, path: Path, agency: str | None) -> dict:
    boundary = f"----mondje{uuid.uuid4().hex}"
    content_type = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    body = bytearray()

    def field(name: str, value: str) -> None:
        body.extend(f"--{boundary}\r\n".encode())
        body.extend(f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode())
        body.extend(value.encode("utf-8"))
        body.extend(b"\r\n")

    if agency:
        field("agency", agency)

    body.extend(f"--{boundary}\r\n".encode())
    body.extend(
        f'Content-Disposition: form-data; name="file"; filename="{path.name}"\r\n'.encode()
    )
    body.extend(f"Content-Type: {content_type}\r\n\r\n".encode())
    body.extend(path.read_bytes())
    body.extend(b"\r\n")
    body.extend(f"--{boundary}--\r\n".encode())

    request = urllib.request.Request(
        url,
        data=bytes(body),
        method="POST",
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    with urllib.request.urlopen(request, timeout=600) as response:
        return json.load(response)


def main() -> None:
    parser = argparse.ArgumentParser(description="Measure a Mondje import end to end.")
    parser.add_argument("--file", type=Path, required=True)
    parser.add_argument("--api", default="http://localhost:5001")
    parser.add_argument("--agency", choices=["Douala", "Yaoundé", "Bafoussam"])
    parser.add_argument("--timeout", type=int, default=900, help="maximum wait in seconds")
    parser.add_argument("--interval", type=float, default=0.5, help="polling interval")
    args = parser.parse_args()

    base = args.api.rstrip("/")
    started = time.perf_counter()
    created = upload(f"{base}/api/Imports", args.file, args.agency)
    accepted = time.perf_counter()

    import_id = created["id"]
    while True:
        status = request_json(f"{base}/api/Imports/{import_id}")
        if status["status"] in {"COMPLETED", "FAILED", "CANCELLED"}:
            break
        if time.perf_counter() - started > args.timeout:
            raise TimeoutError(f"Import {import_id} did not finish within {args.timeout}s")
        time.sleep(args.interval)

    finished = time.perf_counter()
    result = {
        "importId": import_id,
        "fileName": args.file.name,
        "sizeBytes": args.file.stat().st_size,
        "status": status["status"],
        "uploadMs": round((accepted - started) * 1000),
        "totalMs": round((finished - started) * 1000),
        "serverDurationMs": status["durationMs"],
        "totalLines": status["totalLines"],
        "processedLines": status["processedLines"],
        "inserted": status["insertedCount"],
        "duplicates": status["duplicateCount"],
        "errors": status["errorCount"],
        "linesPerSecond": round(
            status["processedLines"] / max(finished - accepted, 0.001)
        ),
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except urllib.error.HTTPError as error:
        print(error.read().decode("utf-8", errors="replace"))
        raise
