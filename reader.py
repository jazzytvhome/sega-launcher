"""
reader.py — turn the raw dump into a readable codebase.

What it does:
  1. Walks ./dump for every *.js file.
  2. Beautifies each one to ./pretty/<same-relative-path>.
     (Original layout preserved so the relative imports in the code still
      make sense when you cross-reference them.)
  3. Writes ./pretty/READ_ME_FIRST.md — an index of the JS files ranked by
     what's most worth reading first, with a 6-line preview of each.

Setup:
    pip install jsbeautifier

Run:
    python reader.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

try:
    import jsbeautifier
except ImportError:
    print("Missing dep. Run:  pip install jsbeautifier")
    sys.exit(1)


ROOT = Path(__file__).parent
DUMP = ROOT / "dump"
PRETTY = ROOT / "pretty"


# Hand-curated priorities — filename stem (lowercase) -> (rank, description).
# Lower rank = read this first. Based on Anslo's Medium post about the
# game's architecture (terrain/road/foliage/settings as the core systems).
KNOWN: dict[str, tuple[int, str]] = {
    "devmidlinegenerator":     (1,  "Road pathfinding — moves a midline through the heightmap 10m at a time, with anti-self-intersection. THE algorithmic core."),
    "driftmasmidlinegenerator":(2,  "Winter/holiday variant of the road generator. Diff against the regular one to see the seasonal overrides."),
    "settingsmanager":         (3,  "Every tunable constant in the game lives here — physics, camera, draw distance, audio. Read this to learn what knobs exist."),
    "isstaticroute":           (4,  "Terrain/road classification helpers — probably how road cells are marked vs ambient terrain."),
    "conifers":                (5,  "Tree placement and instanced rendering. Large because it likely embeds the geometry data."),
    "index":                   (8,  "Bundler entry / barrel re-exports. Skim for top-level structure."),
}


def rank(p: Path) -> tuple[int, str]:
    """Return (priority, description) for a JS file. Lower = more interesting."""
    stem = p.stem.lower()
    # Strip the cache-bust hash: 'foo.abc12345' -> 'foo'
    stem = re.sub(r"\.[0-9a-f]{6,}$", "", stem)
    if stem in KNOWN:
        return KNOWN[stem]
    if p.parent.name == "nodes":
        return (6, "Svelte page node — top-level route component for one screen of the game.")
    if p.parent.name == "chunks":
        return (7, "Bundler chunk — could be a small util or a vendor lib. Open to find out.")
    return (9, "")


def beautify_one(src: Path, dst: Path) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    raw = src.read_text(encoding="utf-8", errors="replace")
    opts = jsbeautifier.default_options()
    opts.indent_size = 2
    opts.max_preserve_newlines = 2
    pretty = jsbeautifier.beautify(raw, opts)
    dst.write_text(pretty, encoding="utf-8")


_BORING = re.compile(
    r"^(var\s+\w{1,3}\s*=|^\s*[\w\$]{1,3}\s*=\s*\(|^\s*[\w\$]{1,3}\s*\(|\}|\)|\(|enumerable|configurable|writable|value:|^Object\.)"
)


def preview(path: Path, lines: int = 8) -> str:
    """Return ~N substantive lines, skipping bundler IIFE / defineProperty header.

    The first ~30 lines of any SvelteKit chunk are boilerplate Object.defineProperty
    plumbing that tells you nothing about the file. We skip until we see something
    with semantic weight — a string literal, a class/function keyword, or any
    identifier with a real word (>=5 chars of letters in a row).
    """
    text = path.read_text(encoding="utf-8", errors="replace")
    out: list[str] = []
    started = False
    for raw_line in text.splitlines():
        s = raw_line.strip()
        if not s:
            continue
        if not started:
            looks_real = (
                re.search(r"\b(class|function|const|export|import)\b", s)
                or '"' in s or "'" in s
                or re.search(r"[A-Za-z]{6,}", s)  # any word-like identifier
            ) and not _BORING.match(s)
            if not looks_real:
                continue
            started = True
        if len(s) > 110:
            s = s[:107] + "..."
        out.append(s)
        if len(out) >= lines:
            break
    return "\n".join(out) if out else "// (no substantive lines found in head — open the file directly)"


def main() -> None:
    if not DUMP.exists():
        print(f"No dump found at {DUMP}. Run dump_site.py first.")
        sys.exit(1)

    js_files = sorted(DUMP.rglob("*.js"))
    if not js_files:
        print(f"No .js files under {DUMP}.")
        sys.exit(1)

    print(f"Beautifying {len(js_files)} JS files -> {PRETTY}/")
    PRETTY.mkdir(exist_ok=True)

    entries: list[tuple[int, str, Path, Path, int]] = []  # (rank, desc, src, dst, size)
    for src in js_files:
        rel = src.relative_to(DUMP)
        dst = PRETTY / rel
        try:
            beautify_one(src, dst)
        except Exception as e:
            print(f"  ! could not beautify {rel}: {e}")
            continue
        r, desc = rank(src)
        entries.append((r, desc, src, dst, src.stat().st_size))

    entries.sort(key=lambda e: (e[0], -e[4]))  # by priority asc, then size desc

    lines: list[str] = [
        "# Slow Roads — guided reading order",
        "",
        f"Generated from `{DUMP.name}/`. {len(entries)} JS files beautified into `pretty/`.",
        "",
        "Files are sorted by how much you'll learn per minute of reading. Start at the top.",
        "Anything ranked 7+ is probably a vendor lib or small util — skim only if needed.",
        "",
    ]

    last_rank = -1
    for r, desc, src, dst, size in entries:
        if r != last_rank:
            lines.append(f"\n## Priority {r}\n")
            last_rank = r
        rel = dst.relative_to(ROOT).as_posix()
        kb = size / 1024
        lines.append(f"### `{rel}`  *(raw {kb:,.1f} KB)*")
        if desc:
            lines.append(desc)
        lines.append("")
        lines.append("```js")
        lines.append(preview(dst))
        lines.append("```")
        lines.append("")

    (PRETTY / "READ_ME_FIRST.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"\nDone. Open {PRETTY / 'READ_ME_FIRST.md'} to start reading.")


if __name__ == "__main__":
    main()
