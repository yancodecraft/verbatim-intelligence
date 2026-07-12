# /// script
# requires-python = ">=3.12"
# dependencies = ["anthropic>=0.75"]
# ///
"""Pipeline spike: compare two theme-discovery strategies on a verbatim corpus.

Throwaway by design (see docs/roadmap.md). This script exists to answer three
questions before slice 4 industrializes anything:
  1. Which grouping strategy produces better themes: per-batch discovery then
     consolidation ("direct"), or taxonomy-first then classification ("taxonomy")?
  2. What do cost and latency look like per analysis?
  3. Does faithfulness-by-reference hold: the LLM only ever selects verbatim
     ids; every quoted text is resolved from the corpus, never regenerated.

Usage (inside the uv container, see spike/README.md):
    uv run pipeline.py --input golden/corpus.json --strategy direct
    uv run pipeline.py --input /corpus/state-of-css-2021/browser_interoperability_features.csv --strategy taxonomy
"""

from __future__ import annotations

import argparse
import csv
import json
import pathlib
import re
import sys
import time

import anthropic

MODEL_PRICES_PER_MTOK = {  # (input $, output $)
    "claude-opus-4-8": (5.0, 25.0),
    "claude-sonnet-5": (3.0, 15.0),
    "claude-haiku-4-5": (1.0, 5.0),
}
DISCOVERY_CHUNK_SIZE = 100
CLASSIFY_CHUNK_SIZE = 40
TAXONOMY_SAMPLE_SIZE = 150
MAX_VERBATIMS_PER_SUMMARY = 120
MAX_THEMES_REPORTED = 12

# Lesson from the golden runs: left to "the dominant language of the corpus",
# the model answered in French on a 95% English corpus. Language is a system
# decision, injected — not inferred by the LLM.
SYSTEM = (
    "You analyze raw customer feedback (verbatims). You never quote or rewrite "
    "verbatim text in your outputs: you refer to verbatims strictly by their "
    "numeric id. Themes must emerge from the data, not from a predefined list. "
    "Write every name, description and summary in {language}."
)


class Stats:
    def __init__(self, model: str, language: str) -> None:
        self.model = model
        self.language = language
        self.calls = 0
        self.input_tokens = 0
        self.output_tokens = 0
        self.started = time.monotonic()

    def add(self, usage) -> None:
        self.calls += 1
        self.input_tokens += usage.input_tokens
        self.output_tokens += usage.output_tokens

    @property
    def cost_usd(self) -> float:
        pin, pout = MODEL_PRICES_PER_MTOK[self.model]
        return (self.input_tokens * pin + self.output_tokens * pout) / 1_000_000

    @property
    def elapsed_s(self) -> float:
        return time.monotonic() - self.started


def ask(client: anthropic.Anthropic, stats: Stats, prompt: str, schema: dict) -> dict:
    """One structured call: returns parsed JSON matching `schema`."""
    response = client.messages.create(
        model=stats.model,
        max_tokens=16000,
        system=SYSTEM.format(language=stats.language),
        messages=[{"role": "user", "content": prompt}],
        output_config={"format": {"type": "json_schema", "schema": schema}},
    )
    stats.add(response.usage)
    text = next(b.text for b in response.content if b.type == "text")
    return json.loads(text)


def load_corpus(path: pathlib.Path) -> list[str]:
    """Return verbatim texts; the line index is the verbatim id everywhere."""
    if path.suffix == ".json":
        data = json.loads(path.read_text(encoding="utf-8"))
        return [row["text"] if isinstance(row, dict) else row for row in data]
    with path.open(encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.DictReader(fh))
    return [row["verbatim"] for row in rows]


def numbered(texts: list[str], ids: list[int]) -> str:
    return "\n".join(f"[{i}] {texts[i]}" for i in ids)


THEMES_SCHEMA = {
    "type": "object",
    "properties": {
        "themes": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "description": {"type": "string"},
                    "verbatim_ids": {"type": "array", "items": {"type": "integer"}},
                },
                "required": ["name", "description", "verbatim_ids"],
                "additionalProperties": False,
            },
        }
    },
    "required": ["themes"],
    "additionalProperties": False,
}

MERGE_SCHEMA = {
    "type": "object",
    "properties": {
        "themes": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "description": {"type": "string"},
                    "source_indexes": {"type": "array", "items": {"type": "integer"}},
                },
                "required": ["name", "description", "source_indexes"],
                "additionalProperties": False,
            },
        }
    },
    "required": ["themes"],
    "additionalProperties": False,
}

SUMMARY_SCHEMA = {
    "type": "object",
    "properties": {
        "summary": {"type": "string"},
        "representative_ids": {"type": "array", "items": {"type": "integer"}},
    },
    "required": ["summary", "representative_ids"],
    "additionalProperties": False,
}


def strategy_direct(client, stats, texts: list[str]) -> list[dict]:
    """Per-batch theme discovery, then one consolidation pass."""
    batch_themes: list[dict] = []
    for start in range(0, len(texts), DISCOVERY_CHUNK_SIZE):
        ids = list(range(start, min(start + DISCOVERY_CHUNK_SIZE, len(texts))))
        result = ask(
            client, stats,
            "Identify the emerging themes (between 3 and 8) in this batch of "
            "customer feedback verbatims. Assign each relevant verbatim id to "
            "the themes it supports (a verbatim may support several themes; "
            "off-topic or empty verbatims belong to none).\n\n"
            + numbered(texts, ids),
            THEMES_SCHEMA,
        )
        valid = set(ids)
        for theme in result["themes"]:
            theme["verbatim_ids"] = [i for i in theme["verbatim_ids"] if i in valid]
        batch_themes.extend(result["themes"])
        print(f"  discovery batch {start // DISCOVERY_CHUNK_SIZE + 1}: "
              f"{len(result['themes'])} themes", file=sys.stderr)

    catalog = "\n".join(
        f"[{i}] {t['name']} — {t['description']} ({len(t['verbatim_ids'])} verbatims)"
        for i, t in enumerate(batch_themes)
    )
    merged = ask(
        client, stats,
        "These candidate themes were discovered independently on batches of the "
        "same corpus. Merge duplicates and near-duplicates into a consolidated "
        "list of distinct themes (keep it focused: the fewest themes that "
        "faithfully cover the candidates). For each consolidated theme, list the "
        "indexes of the candidate themes it absorbs.\n\n" + catalog,
        MERGE_SCHEMA,
    )
    themes = []
    for m in merged["themes"]:
        ids = sorted({
            vid
            for idx in m["source_indexes"] if 0 <= idx < len(batch_themes)
            for vid in batch_themes[idx]["verbatim_ids"]
        })
        themes.append({"name": m["name"], "description": m["description"], "verbatim_ids": ids})
    return themes


def strategy_taxonomy(client, stats, texts: list[str]) -> list[dict]:
    """Taxonomy proposed on a sample, then full classification against it."""
    step = max(1, len(texts) // TAXONOMY_SAMPLE_SIZE)
    sample_ids = list(range(0, len(texts), step))[:TAXONOMY_SAMPLE_SIZE]
    taxonomy = ask(
        client, stats,
        "From this representative sample of customer feedback verbatims, "
        "propose the taxonomy of emerging themes (between 4 and 10 themes) "
        "that best covers the corpus. Do not assign verbatims yet: "
        "return themes with an empty verbatim_ids list.\n\n"
        + numbered(texts, sample_ids),
        THEMES_SCHEMA,
    )
    names = [t["name"] for t in taxonomy["themes"]]
    print(f"  taxonomy: {len(names)} themes proposed", file=sys.stderr)

    # Two failed formats taught us the classification shape. (1) One
    # {verbatim_id, themes} object per verbatim: the model often returns a
    # single item for an 80-verbatim chunk (silent under-fill), and the API
    # rejects minItems > 1 so the schema cannot enforce exhaustiveness.
    # (2) Despite enums, the model emits case-variants of theme names.
    # So: same ids-per-theme shape that never failed in the direct strategy,
    # an explicit "other" bucket to make coverage observable, case-insensitive
    # name matching, and a catch-up pass on whatever remains uncovered.
    classify_schema = {
        "type": "object",
        "properties": {
            "assignments": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "theme": {"type": "string", "enum": [*names, "other"]},
                        "verbatim_ids": {"type": "array", "items": {"type": "integer"}},
                    },
                    "required": ["theme", "verbatim_ids"],
                    "additionalProperties": False,
                },
            }
        },
        "required": ["assignments"],
        "additionalProperties": False,
    }
    taxonomy_text = "\n".join(
        f"- {t['name']}: {t['description']}" for t in taxonomy["themes"]
    )
    assigned: dict[str, list[int]] = {name: [] for name in names}
    by_folded = {name.casefold(): name for name in names}
    unknown_names = 0
    pending = list(range(len(texts)))
    for attempt in range(2):
        leftover: list[int] = []
        for start in range(0, len(pending), CLASSIFY_CHUNK_SIZE):
            ids = pending[start:start + CLASSIFY_CHUNK_SIZE]
            result = ask(
                client, stats,
                f"Taxonomy of themes:\n{taxonomy_text}\n\n"
                "For each theme (including \"other\"), list the ids of the "
                "verbatims below that support it. Every verbatim id must "
                "appear under at least one theme; use \"other\" for verbatims "
                "no theme covers. A verbatim may support several themes. "
                "Never return a theme with an empty verbatim_ids list.\n\n"
                + numbered(texts, ids),
                classify_schema,
            )
            valid, seen = set(ids), set()
            for a in result["assignments"]:
                kept = [i for i in a["verbatim_ids"] if i in valid]
                seen.update(kept)
                if a["theme"].casefold() == "other":
                    continue
                known = by_folded.get(a["theme"].casefold())
                if known is None:
                    unknown_names += 1
                else:
                    assigned[known].extend(kept)
            leftover.extend(i for i in ids if i not in seen)
            print(f"  classification pass {attempt + 1}, batch "
                  f"{start // CLASSIFY_CHUNK_SIZE + 1}: {len(seen)}/{len(ids)}",
                  file=sys.stderr)
            if not seen:
                shape = [(a["theme"], a["verbatim_ids"][:6], len(a["verbatim_ids"]))
                         for a in result["assignments"][:4]]
                print(f"    DEBUG empty batch (expected ids {ids[0]}..{ids[-1]}): "
                      f"{len(result['assignments'])} assignments, shape {shape}",
                      file=sys.stderr)
        pending = leftover
        if not pending:
            break
    if pending:
        print(f"  WARNING: {len(pending)} verbatims never classified", file=sys.stderr)
    if unknown_names:
        print(f"  WARNING: {unknown_names} assignments to theme names outside "
              "the taxonomy, dropped", file=sys.stderr)

    return [
        {"name": t["name"], "description": t["description"],
         "verbatim_ids": sorted(set(assigned[t["name"]]))}
        for t in taxonomy["themes"]
    ]


def summarize(client, stats, texts: list[str], theme: dict) -> tuple[dict, int]:
    """Summary + representative ids for one theme. Returns (result, violations).

    Faithfulness by construction: the LLM returns ids only; quoted texts are
    resolved from the corpus by the caller. Ids outside the theme are dropped
    and counted as violations.
    """
    ids = theme["verbatim_ids"][:MAX_VERBATIMS_PER_SUMMARY]
    result = ask(
        client, stats,
        f"Theme: {theme['name']} — {theme['description']}\n\n"
        "Write a faithful summary of this theme (3-5 sentences, grounded only "
        "in the verbatims below, no extrapolation), and select the 3 to 5 most "
        "representative verbatim ids.\n\n" + numbered(texts, ids),
        SUMMARY_SCHEMA,
    )
    valid = set(ids)
    kept = [i for i in result["representative_ids"] if i in valid]
    violations = len(result["representative_ids"]) - len(kept)
    result["representative_ids"] = kept
    return result, violations


def evaluate_golden(themes: list[dict], corpus_path: pathlib.Path) -> list[str]:
    """Compare discovered themes against the planted ones (golden corpus only)."""
    rows = json.loads(corpus_path.read_text(encoding="utf-8"))
    expected_ids: dict[str, set[int]] = {}
    for i, row in enumerate(rows):
        for key in row.get("expected", []):
            expected_ids.setdefault(key, set()).add(i)
    planted = json.loads(
        (corpus_path.parent / "themes.json").read_text(encoding="utf-8")
    )
    noise_ids = {i for i, row in enumerate(rows) if not row.get("expected")}

    lines = ["| planted theme | found | coverage | as |", "|---|---|---|---|"]
    for key, spec in planted.items():
        matches = [
            t for t in themes
            if any(re.search(rf"\b{re.escape(kw)}", (t["name"] + " " + t["description"]).lower())
                   for kw in spec["keywords"])
        ]
        if not matches:
            lines.append(f"| {spec['label']} | **MISSED** | — | — |")
            continue
        covered = set().union(*(set(t["verbatim_ids"]) for t in matches))
        target = expected_ids.get(key, set())
        pct = 100 * len(covered & target) // max(1, len(target))
        names = ", ".join(t["name"] for t in matches)
        lines.append(f"| {spec['label']} | yes | {pct}% of {len(target)} | {names} |")

    all_assigned = set().union(*(set(t["verbatim_ids"]) for t in themes)) if themes else set()
    lines.append("")
    lines.append(f"Noise verbatims swept into themes: {len(all_assigned & noise_ids)}"
                 f"/{len(noise_ids)}")
    return lines


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=pathlib.Path)
    parser.add_argument("--strategy", required=True, choices=["direct", "taxonomy"])
    parser.add_argument("--model", default="claude-opus-4-8",
                        choices=sorted(MODEL_PRICES_PER_MTOK))
    parser.add_argument("--language", default="English",
                        help="output language for themes and summaries")
    parser.add_argument("--out-dir", default=pathlib.Path("results"), type=pathlib.Path)
    args = parser.parse_args()

    texts = load_corpus(args.input)
    print(f"{len(texts)} verbatims loaded from {args.input}", file=sys.stderr)

    client = anthropic.Anthropic()
    stats = Stats(args.model, args.language)
    run = strategy_direct if args.strategy == "direct" else strategy_taxonomy
    themes = run(client, stats, texts)
    themes = [t for t in themes if t["verbatim_ids"]]
    themes.sort(key=lambda t: len(t["verbatim_ids"]), reverse=True)
    themes = themes[:MAX_THEMES_REPORTED]

    total_violations = 0
    for theme in themes:
        result, violations = summarize(client, stats, texts, theme)
        theme["summary"] = result["summary"]
        theme["representative_ids"] = result["representative_ids"]
        total_violations += violations

    report = [
        f"# Spike run — {args.strategy} — {args.input.name}",
        "",
        f"- corpus: {len(texts)} verbatims",
        f"- model: {stats.model}",
        f"- API calls: {stats.calls}, tokens in/out: "
        f"{stats.input_tokens}/{stats.output_tokens}",
        f"- cost: ${stats.cost_usd:.3f}, wall time: {stats.elapsed_s:.0f}s",
        f"- faithfulness violations (ids cited outside their theme, dropped): "
        f"{total_violations}",
        "",
    ]
    for theme in themes:
        report += [
            f"## {theme['name']} ({len(theme['verbatim_ids'])} verbatims)",
            "",
            theme["description"], "",
            f"**Summary:** {theme['summary']}", "",
        ]
        # The quotes below are resolved from the corpus by id — the whole point.
        report += [f"> [{i}] {texts[i]}" for i in theme["representative_ids"]]
        report.append("")

    if args.input.suffix == ".json":
        report += ["## Golden evaluation", ""]
        report += evaluate_golden(themes, args.input)

    args.out_dir.mkdir(parents=True, exist_ok=True)
    stem = f"{args.strategy}-{args.input.stem}-{args.model}"
    (args.out_dir / f"{stem}.md").write_text("\n".join(report), encoding="utf-8")
    (args.out_dir / f"{stem}.json").write_text(
        json.dumps(themes, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    print("\n".join(report))


if __name__ == "__main__":
    main()
