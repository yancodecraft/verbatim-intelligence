#!/usr/bin/env python3
"""Deterministic documentation consistency checks, run by the pre-commit hook.

Designed to run inside a container (see scripts/githooks/pre-commit): no git
calls here — the hook mounts the repo at the working directory and passes the
staged file list via the STAGED_FILES environment variable (one path per
line; unset = skip the staged-files check).

Checks (blocking):
  1. Internal markdown links point to existing files and headings.
  2. Canonical definitions live in exactly one file (others must link to it).
  3. Vocabulary banned by the glossary does not reappear in the docs.

Checks (warning only):
  4. A commit touching docs/ or CLAUDE.md without touching JOURNAL.md.

Fenced code blocks are ignored by every check. Stdlib only — no dependencies.
"""

from __future__ import annotations

import os
import re
import sys
from pathlib import Path

ROOT = Path(os.environ.get("REPO_ROOT", os.getcwd())).resolve()

LINK_RE = re.compile(r"\[[^\]]*\]\(([^)\s]+)\)")
HEADING_RE = re.compile(r"^#{1,6}\s+(.*)$")
GUILLEMETS_RE = re.compile(r"«[^»]*»")

# A canonical phrase may only appear in its owning file; other docs link to it.
CANONICAL_PHRASES = {
    "une tranche est finie quand": "docs/practices.md",
}

# Terms the glossary explicitly rejected. Allowed in the glossary itself
# (which explains the rejection), in the journal (history), and when quoted
# in « guillemets » (decision rationale).
BANNED_TERMS = re.compile(r"\bjobs?\b", re.IGNORECASE)
BANNED_TERMS_ALLOWED_FILES = {"docs/glossary.md", "JOURNAL.md"}

_read_errors: list[str] = []
_text_cache: dict[Path, str] = {}


def strip_code_blocks(raw: str) -> str:
    out, fenced = [], False
    for line in raw.splitlines():
        if line.lstrip().startswith(("```", "~~~")):
            fenced = not fenced
            continue
        out.append(line if not fenced else "")
    return "\n".join(out)


def text_of(path: Path) -> str:
    """Markdown text with fenced code blocks blanked; '' on decode failure."""
    if path not in _text_cache:
        try:
            raw = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            _read_errors.append(f"{path.relative_to(ROOT)}: not valid UTF-8")
            raw = ""
        _text_cache[path] = strip_code_blocks(raw)
    return _text_cache[path]


def doc_files() -> list[Path]:
    files = [ROOT / "CLAUDE.md", ROOT / "JOURNAL.md"]
    files += sorted((ROOT / "docs").glob("*.md"))
    return [f for f in files if f.exists()]


def github_slug(heading: str) -> str:
    out: list[str] = []
    for ch in heading.strip().lower():
        if ch.isalnum() or ch in "_-":
            out.append(ch)
        elif ch == " ":
            out.append("-")
    return "".join(out)


def headings_of(path: Path) -> set[str]:
    slugs = set()
    for line in text_of(path).splitlines():
        m = HEADING_RE.match(line)
        if m:
            slugs.add(github_slug(m.group(1)))
    return slugs


def check_links(files: list[Path]) -> list[str]:
    errors = []
    for f in files:
        for target in LINK_RE.findall(text_of(f)):
            if target.startswith(("http://", "https://", "mailto:")):
                continue
            path_part, _, anchor = target.partition("#")
            dest = (f.parent / path_part).resolve() if path_part else f
            if path_part and not dest.exists():
                errors.append(f"{f.relative_to(ROOT)}: broken link -> {target}")
                continue
            if anchor and dest.suffix == ".md" and anchor not in headings_of(dest):
                errors.append(
                    f"{f.relative_to(ROOT)}: broken anchor -> {target}"
                )
    return errors


def check_canonical(files: list[Path]) -> list[str]:
    errors = []
    for phrase, owner in CANONICAL_PHRASES.items():
        for f in files:
            rel = str(f.relative_to(ROOT))
            if rel == owner:
                continue
            if phrase in text_of(f).lower():
                errors.append(
                    f"{rel}: redefines canonical content owned by {owner}"
                    f" (phrase: '{phrase}') — link to it instead"
                )
    return errors


def check_banned_terms(files: list[Path]) -> list[str]:
    errors = []
    for f in files:
        rel = str(f.relative_to(ROOT))
        if rel in BANNED_TERMS_ALLOWED_FILES:
            continue
        for n, line in enumerate(text_of(f).splitlines(), start=1):
            scrubbed = GUILLEMETS_RE.sub("", line)
            if BANNED_TERMS.search(scrubbed):
                errors.append(
                    f"{rel}:{n}: banned term (see glossary) -> {line.strip()}"
                )
    return errors


def check_journal_touched() -> list[str]:
    staged_env = os.environ.get("STAGED_FILES")
    if staged_env is None:
        return []
    staged = [line for line in staged_env.splitlines() if line.strip()]
    doc_changes = [
        p for p in staged if p.startswith("docs/") or p == "CLAUDE.md"
    ]
    if doc_changes and "JOURNAL.md" not in staged:
        return [
            "docs changed without a JOURNAL.md entry "
            f"({', '.join(doc_changes)}) — fine for typos, "
            "expected for structural decisions"
        ]
    return []


def main() -> int:
    if not (ROOT / "CLAUDE.md").exists():
        print(
            "error: CLAUDE.md not found — run from the repository root "
            "(or set REPO_ROOT)",
            file=sys.stderr,
        )
        return 1

    files = doc_files()
    errors = (
        check_links(files)
        + check_canonical(files)
        + check_banned_terms(files)
        + _read_errors
    )
    warnings = check_journal_touched()

    for w in warnings:
        print(f"warning: {w}")
    for e in errors:
        print(f"error: {e}", file=sys.stderr)

    if errors:
        print(f"\ndocs check failed ({len(errors)} error(s))", file=sys.stderr)
        return 1
    print(f"docs check passed ({len(files)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
