"""The analysis pipeline: discovery by batches, consolidation, syntheses.

Industrialization of the spike's winning strategy (see docs/architecture.md
and the journal entry of 2026-07-12): each batch of verbatims yields
candidate themes with the ids that support them, one consolidation pass
merges duplicates across batches, then each theme gets its synthesis and its
representative ids. The LLM only ever selects ids; every id it returns is
checked against what was actually shown, and every cited verbatim is a
foreign key to its original row.
"""

import logging
import os
import uuid
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Any

from ai_worker.language import detect_language
from ai_worker.llm import SupportsAsk, build_llm, model_from_env

if TYPE_CHECKING:
    import psycopg

logger = logging.getLogger(__name__)

DISCOVERY_BATCH_SIZE = 100
MAX_VERBATIMS_PER_SUMMARY = 120
MAX_THEMES = 12

DEFAULT_COST_CAP_USD = 1.0

# $ per million tokens (input, output). The database stores tokens, not an
# amount: prices change, measured spend stays true — the cap arithmetic
# lives here only.
MODEL_PRICES_PER_MTOK = {
    "claude-opus-4-8": (5.0, 25.0),
    "claude-sonnet-5": (3.0, 15.0),
    "claude-haiku-4-5": (1.0, 5.0),
}

THEMES_SCHEMA: dict[str, Any] = {
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

MERGE_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "themes": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "description": {"type": "string"},
                    "source_indexes": {
                        "type": "array",
                        "items": {"type": "integer"},
                    },
                },
                "required": ["name", "description", "source_indexes"],
                "additionalProperties": False,
            },
        }
    },
    "required": ["themes"],
    "additionalProperties": False,
}

SUMMARY_SCHEMA: dict[str, Any] = {
    "type": "object",
    "properties": {
        "summary": {"type": "string"},
        "representative_ids": {"type": "array", "items": {"type": "integer"}},
    },
    "required": ["summary", "representative_ids"],
    "additionalProperties": False,
}


class CostCapError(Exception):
    """The per-analysis cost cap was reached; the analysis must fail visibly."""


@dataclass
class _Theme:
    name: str
    description: str
    verbatim_ids: list[int]
    synthesis: str = ""
    representative_ids: list[int] = field(default_factory=list)


class _CostGuard:
    """Blocks the next LLM call once the analysis has spent its cap.

    Counts the spend already recorded on the analysis, previous attempts
    included: the cap is per analysis, not per attempt.
    """

    def __init__(
        self, model: str, cap_usd: float, input_tokens: int, output_tokens: int
    ) -> None:
        if model not in MODEL_PRICES_PER_MTOK:
            message = (
                f"Unknown model {model!r}: no price is known, the cost cap"
                " cannot be enforced."
            )
            raise CostCapError(message)
        self._prices = MODEL_PRICES_PER_MTOK[model]
        self._cap_usd = cap_usd
        self._input_tokens = input_tokens
        self._output_tokens = output_tokens

    def add(self, input_tokens: int, output_tokens: int) -> None:
        self._input_tokens += input_tokens
        self._output_tokens += output_tokens

    @property
    def cost_usd(self) -> float:
        price_in, price_out = self._prices
        return (
            self._input_tokens * price_in + self._output_tokens * price_out
        ) / 1_000_000

    def check(self) -> None:
        if self.cost_usd >= self._cap_usd:
            message = (
                f"Analysis stopped at its cost cap (${self._cap_usd:.2f}):"
                f" ${self.cost_usd:.2f} already spent."
            )
            raise CostCapError(message)


def run(
    connection: psycopg.Connection,
    analysis_id: uuid.UUID,
    llm: SupportsAsk | None = None,
) -> None:
    """Run the corpus of a claimed analysis through the full pipeline."""
    verbatims = connection.execute(
        "SELECT id, text FROM verbatims WHERE analysis_id = %s ORDER BY position",
        (analysis_id,),
    ).fetchall()
    if not verbatims:
        logger.info("analysis %s has no verbatims, nothing to analyze", analysis_id)
        return

    texts: list[str] = [text for _, text in verbatims]
    guard = _load_guard(connection, analysis_id)
    # Fail on a spent cap before building a client: cheaper and clearer.
    guard.check()
    if llm is None:
        llm = build_llm(language=detect_language(texts))

    execution = _Execution(connection, analysis_id, llm, guard)
    candidates = execution.discover(texts)
    themes = _consolidate_locally(candidates, execution.merge(candidates))
    themes.sort(key=lambda theme: len(theme.verbatim_ids), reverse=True)
    themes = themes[:MAX_THEMES]
    for theme in themes:
        execution.summarize(texts, theme)
    _write_results(connection, analysis_id, [vid for vid, _ in verbatims], themes)


class _Execution:
    """One pipeline run: asks the LLM, meters spend, heartbeats, progresses."""

    def __init__(
        self,
        connection: psycopg.Connection,
        analysis_id: uuid.UUID,
        llm: SupportsAsk,
        guard: _CostGuard,
    ) -> None:
        self._connection = connection
        self._analysis_id = analysis_id
        self._llm = llm
        self._guard = guard

    def discover(self, texts: list[str]) -> list[_Theme]:
        """Candidate themes per batch; every returned id is checked."""
        candidates: list[_Theme] = []
        for start in range(0, len(texts), DISCOVERY_BATCH_SIZE):
            ids = list(range(start, min(start + DISCOVERY_BATCH_SIZE, len(texts))))
            data = self._ask(
                "Identify the emerging themes (between 3 and 8) in this batch"
                " of customer feedback verbatims. Assign each relevant verbatim"
                " id to the themes it supports (a verbatim may support several"
                " themes; off-topic or empty verbatims belong to none).\n\n"
                + _numbered(texts, ids),
                THEMES_SCHEMA,
                processed=ids[-1] + 1,
            )
            valid = set(ids)
            for raw in data["themes"]:
                kept = [i for i in raw["verbatim_ids"] if i in valid]
                dropped = len(raw["verbatim_ids"]) - len(kept)
                if dropped:
                    logger.warning(
                        "analysis %s: %s ids outside the batch dropped from"
                        " candidate %r",
                        self._analysis_id,
                        dropped,
                        raw["name"],
                    )
                candidates.append(
                    _Theme(
                        name=raw["name"],
                        description=raw["description"],
                        verbatim_ids=kept,
                    )
                )
        return candidates

    def merge(self, candidates: list[_Theme]) -> list[dict[str, Any]]:
        """One consolidation pass across all batch candidates."""
        if not candidates:
            return []
        catalog = "\n".join(
            f"[{i}] {theme.name} — {theme.description}"
            f" ({len(theme.verbatim_ids)} verbatims)"
            for i, theme in enumerate(candidates)
        )
        data = self._ask(
            "These candidate themes were discovered independently on batches"
            " of the same corpus. Merge duplicates and near-duplicates into a"
            " consolidated list of distinct themes (keep it focused: the"
            " fewest themes that faithfully cover the candidates). For each"
            " consolidated theme, list the indexes of the candidate themes it"
            " absorbs.\n\n" + catalog,
            MERGE_SCHEMA,
        )
        themes: list[dict[str, Any]] = data["themes"]
        return themes

    def summarize(self, texts: list[str], theme: _Theme) -> None:
        """Synthesis + representative ids; ids outside the theme are dropped."""
        ids = theme.verbatim_ids[:MAX_VERBATIMS_PER_SUMMARY]
        data = self._ask(
            f"Theme: {theme.name} — {theme.description}\n\n"
            "Write a faithful summary of this theme (3-5 sentences, grounded"
            " only in the verbatims below, no extrapolation), and select the"
            " 3 to 5 most representative verbatim ids.\n\n" + _numbered(texts, ids),
            SUMMARY_SCHEMA,
        )
        valid = set(ids)
        kept = list(dict.fromkeys(i for i in data["representative_ids"] if i in valid))
        dropped = len(data["representative_ids"]) - len(kept)
        if dropped:
            logger.warning(
                "analysis %s: %s representative ids outside theme %r dropped",
                self._analysis_id,
                dropped,
                theme.name,
            )
        theme.synthesis = data["summary"]
        theme.representative_ids = kept

    def _ask(
        self,
        prompt: str,
        schema: dict[str, Any],
        processed: int | None = None,
    ) -> dict[str, Any]:
        self._guard.check()
        reply = self._llm.ask(prompt, schema)
        self._guard.add(reply.input_tokens, reply.output_tokens)
        # One statement doubles as the heartbeat: spend, progress and sign of
        # life move together, between every LLM call.
        self._connection.execute(
            "UPDATE analyses SET heartbeat_at = now(),"
            " input_tokens = input_tokens + %s,"
            " output_tokens = output_tokens + %s,"
            " processed_count = coalesce(%s, processed_count)"
            " WHERE id = %s AND status = 'running'",
            (reply.input_tokens, reply.output_tokens, processed, self._analysis_id),
        )
        self._connection.commit()
        return reply.data


def _numbered(texts: list[str], ids: list[int]) -> str:
    return "\n".join(f"[{i}] {texts[i]}" for i in ids)


def _consolidate_locally(
    candidates: list[_Theme], merged: list[dict[str, Any]]
) -> list[_Theme]:
    """Union the supports of the absorbed candidates; unknown indexes drop."""
    themes = []
    for raw in merged:
        ids = sorted(
            {
                verbatim_id
                for index in raw["source_indexes"]
                if 0 <= index < len(candidates)
                for verbatim_id in candidates[index].verbatim_ids
            }
        )
        themes.append(
            _Theme(name=raw["name"], description=raw["description"], verbatim_ids=ids)
        )
    return [theme for theme in themes if theme.verbatim_ids]


def _load_guard(connection: psycopg.Connection, analysis_id: uuid.UUID) -> _CostGuard:
    row = connection.execute(
        "SELECT input_tokens, output_tokens FROM analyses WHERE id = %s",
        (analysis_id,),
    ).fetchone()
    if row is None:  # pragma: no cover — the caller just claimed it
        message = f"analysis {analysis_id} vanished mid-claim"
        raise RuntimeError(message)
    cap_usd = float(os.environ.get("ANALYSIS_COST_CAP_USD", DEFAULT_COST_CAP_USD))
    return _CostGuard(model_from_env(), cap_usd, row[0], row[1])


def _write_results(
    connection: psycopg.Connection,
    analysis_id: uuid.UUID,
    verbatim_uuids: list[uuid.UUID],
    themes: list[_Theme],
) -> None:
    """Persist themes and attachments in one transaction.

    The index → uuid mapping happens here and nowhere else: the LLM selected
    indexes into what it was shown, the database receives foreign keys.
    """
    with connection.cursor() as cursor:
        for position, theme in enumerate(themes):
            theme_id = uuid.uuid4()
            cursor.execute(
                "INSERT INTO themes (id, analysis_id, name, synthesis, position)"
                " VALUES (%s, %s, %s, %s, %s)",
                (theme_id, analysis_id, theme.name[:200], theme.synthesis, position),
            )
            ranks = {
                verbatim_index: rank
                for rank, verbatim_index in enumerate(theme.representative_ids)
            }
            cursor.executemany(
                "INSERT INTO theme_verbatims (theme_id, verbatim_id, rank)"
                " VALUES (%s, %s, %s)",
                [
                    (theme_id, verbatim_uuids[index], ranks.get(index))
                    for index in theme.verbatim_ids
                ],
            )
    connection.commit()
