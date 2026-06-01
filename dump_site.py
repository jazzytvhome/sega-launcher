"""
dump_site.py — paste a URL, get every client-side asset on disk.

Works on plain HTML sites, but specifically built for modern SPAs / WebGL games
(slowroads.io, three.js demos, etc.) where most assets are fetched at runtime
by JavaScript and a static crawler would miss them.

How it works
------------
1. Launches a real Chromium browser via Playwright.
2. Hooks the `response` event on the page so every network response — initial
   HTML, JS chunks, GLB models, KTX2 textures, audio, JSON manifests — gets
   written to disk preserving the URL path structure.
3. Stays open so you can interact with the page (drive the car, scroll, click
   tabs) to trigger lazy-loaded assets. Press Enter in the terminal to stop.

First-time setup
----------------
    pip install playwright
    playwright install chromium

Run
---
    python dump_site.py
    # then paste a URL when prompted, e.g. https://slowroads.io/
"""

from __future__ import annotations

import sys
import threading
from pathlib import Path
from urllib.parse import urlparse, unquote

from playwright.sync_api import sync_playwright, Response


def safe_path(out_root: Path, response_url: str) -> Path | None:
    """Map https://host/foo/bar.js  ->  out_root/host/foo/bar.js

    Returns None for non-http(s) schemes (data:, blob:, chrome-extension:).
    Query strings are folded into the filename so cache-busted variants don't
    overwrite each other.
    """
    parsed = urlparse(response_url)
    if parsed.scheme not in ("http", "https"):
        return None

    path_part = unquote(parsed.path) or "/"
    # Directory-style URL -> index.html
    if path_part.endswith("/"):
        path_part += "index.html"

    target = out_root / parsed.hostname / path_part.lstrip("/")

    if parsed.query:
        # Short stable tag from the querystring so ?v=2 and ?v=3 don't collide.
        tag = hex(abs(hash(parsed.query)))[2:10]
        target = target.with_name(f"{target.stem}__{tag}{target.suffix}")

    return target


def dump(url: str, out_dir: Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    seen: set[str] = set()
    counters = {"saved": 0, "skipped": 0}

    def on_response(response: Response) -> None:
        u = response.url
        if u in seen:
            return
        seen.add(u)

        target = safe_path(out_dir, u)
        if target is None:
            counters["skipped"] += 1
            return

        # Redirects have no body; skip them (the followed response will arrive
        # separately and get saved on its own).
        if 300 <= response.status < 400:
            counters["skipped"] += 1
            return

        try:
            body = response.body()
        except Exception:
            # Some responses (preflight, opaque, aborted) have no readable body.
            counters["skipped"] += 1
            return

        try:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(body)
            counters["saved"] += 1
            if counters["saved"] % 25 == 0:
                print(f"  saved {counters['saved']} files...", flush=True)
        except OSError as e:
            # Filename too long, invalid char on Windows, etc.
            print(f"  ! could not write {target}: {e}", flush=True)
            counters["skipped"] += 1

    with sync_playwright() as p:
        browser = p.chromium.launch(
            headless=False,
            args=["--disable-application-cache"],
        )
        context = browser.new_context()
        # Force every asset through the network (and through our hook).
        context.set_extra_http_headers({"Cache-Control": "no-cache"})
        page = context.new_page()
        page.on("response", on_response)

        print(f"\nNavigating to {url} ...")
        try:
            page.goto(url, wait_until="networkidle", timeout=120_000)
        except Exception as e:
            print(f"  ! navigation warning: {e} (continuing — partial loads still capture)")

        print(
            "\nPage loaded. The browser window stays open so you can interact"
            "\nwith the site to trigger lazy-loaded assets:"
            "\n  - For slowroads-style games: click the canvas and drive around."
            "\n  - For a normal SPA: click through routes/tabs."
            "\nWhen you're done, press Enter here to finish the dump."
        )

        # Wait for Enter in a separate thread so the browser stays interactive.
        done = threading.Event()
        def _wait_for_enter() -> None:
            try:
                input()
            except EOFError:
                pass  # stdin closed (non-interactive run) — just stop the dump
            done.set()

        threading.Thread(target=_wait_for_enter, daemon=True).start()
        while not done.is_set():
            try:
                page.wait_for_timeout(500)  # yields back to the browser event loop
            except Exception:
                break  # window was closed manually

        browser.close()

    print(
        f"\nDone. {counters['saved']} files written under {out_dir}"
        f" ({counters['skipped']} skipped)."
    )


def main() -> int:
    print("=== Site dumper ===")
    raw = sys.argv[1] if len(sys.argv) > 1 else input("URL to dump: ")
    # Strip ASCII whitespace plus the UTF-8 BOM and zero-width chars that
    # sneak in when URLs are pasted from browsers / pipes.
    url = raw.strip().strip("﻿​‌‍").strip()
    if not url:
        print("No URL provided.")
        return 1
    if not url.startswith(("http://", "https://")):
        url = "https://" + url

    # safe_path() already prepends the hostname, so out_dir is just the parent.
    out_dir = Path(__file__).parent / "dump"
    dump(url, out_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
