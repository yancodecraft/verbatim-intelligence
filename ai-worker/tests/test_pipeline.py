from typing import TYPE_CHECKING, Any

import pytest
from conftest import fetch_analysis, insert_analysis, insert_verbatims

from ai_worker.llm import LlmReply
from ai_worker.pipeline import CostCapError, run
from ai_worker.processing import process_analysis

if TYPE_CHECKING:
    import uuid

    import psycopg

TOKENS_PER_CALL = (1000, 100)

CORPUS = [
    "too slow",
    "crashes on save",
    "slow again",
    "love it",
    "unrelated noise",
]


class ScriptedLlm:
    """Replays canned structured outputs; records every prompt it receives."""

    def __init__(self, replies: list[dict[str, Any]]) -> None:
        self._replies = list(replies)
        self.prompts: list[str] = []

    def ask(self, prompt: str, schema: dict[str, Any]) -> LlmReply:
        del schema
        self.prompts.append(prompt)
        assert self._replies, "the pipeline asked more than the test scripted"
        return LlmReply(
            data=self._replies.pop(0),
            input_tokens=TOKENS_PER_CALL[0],
            output_tokens=TOKENS_PER_CALL[1],
        )


def seed_running_analysis(
    connection: psycopg.Connection, texts: list[str]
) -> tuple[uuid.UUID, list[uuid.UUID]]:
    analysis_id = insert_analysis(connection, "running", attempts=1)
    return analysis_id, insert_verbatims(connection, analysis_id, texts)


def themes_of(
    connection: psycopg.Connection, analysis_id: uuid.UUID
) -> list[tuple[str, str, int]]:
    return [
        (row[0], row[1], row[2])
        for row in connection.execute(
            "SELECT name, synthesis, position FROM themes"
            " WHERE analysis_id = %s ORDER BY position",
            (analysis_id,),
        ).fetchall()
    ]


def attachments_of(
    connection: psycopg.Connection, analysis_id: uuid.UUID
) -> set[tuple[str, uuid.UUID, int | None]]:
    return {
        (row[0], row[1], row[2])
        for row in connection.execute(
            "SELECT t.name, tv.verbatim_id, tv.rank"
            " FROM theme_verbatims tv JOIN themes t ON t.id = tv.theme_id"
            " WHERE t.analysis_id = %s",
            (analysis_id,),
        ).fetchall()
    }


def test_pipeline_writes_themes_syntheses_and_citations_by_reference(
    connection: psycopg.Connection,
) -> None:
    analysis_id, verbatim_ids = seed_running_analysis(connection, CORPUS)
    llm = ScriptedLlm(
        [
            # Discovery (one batch): id 999 does not exist and must be dropped.
            {
                "themes": [
                    {
                        "name": "Perf",
                        "description": "speed complaints",
                        "verbatim_ids": [0, 2, 999],
                    },
                    {
                        "name": "Crash",
                        "description": "crashes",
                        "verbatim_ids": [1],
                    },
                ]
            },
            # Consolidation: candidate index 7 does not exist, dropped too.
            {
                "themes": [
                    {
                        "name": "Crashes",
                        "description": "it crashes",
                        "source_indexes": [1],
                    },
                    {
                        "name": "Performance",
                        "description": "it is slow",
                        "source_indexes": [0, 7],
                    },
                ]
            },
            # Syntheses, in support order (Performance covers 2 verbatims,
            # Crashes 1). Representative id 404 is outside the theme: dropped.
            {"summary": "Speed is a problem.", "representative_ids": [2, 0, 404]},
            {"summary": "It crashes.", "representative_ids": [1]},
        ]
    )

    run(connection, analysis_id, llm=llm)

    # Themes ordered by support, position decided by the worker.
    assert themes_of(connection, analysis_id) == [
        ("Performance", "Speed is a problem.", 0),
        ("Crashes", "It crashes.", 1),
    ]
    # Citations are references: rank orders the cited ones, NULL = support.
    assert attachments_of(connection, analysis_id) == {
        ("Performance", verbatim_ids[2], 0),
        ("Performance", verbatim_ids[0], 1),
        ("Crashes", verbatim_ids[1], 0),
    }
    # Progress and spend are in the database.
    row = connection.execute(
        "SELECT processed_count, input_tokens, output_tokens FROM analyses"
        " WHERE id = %s",
        (analysis_id,),
    ).fetchone()
    assert row == (len(CORPUS), 4 * TOKENS_PER_CALL[0], 4 * TOKENS_PER_CALL[1])
    # The prompt numbers verbatims by index — never exposes database ids.
    assert "[0] too slow" in llm.prompts[0]


def test_pipeline_processes_the_corpus_in_batches(
    connection: psycopg.Connection, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr("ai_worker.pipeline.DISCOVERY_BATCH_SIZE", 2)
    analysis_id, _ = seed_running_analysis(connection, CORPUS)
    llm = ScriptedLlm([{"themes": []}, {"themes": []}, {"themes": []}])

    run(connection, analysis_id, llm=llm)

    # 5 verbatims in batches of 2: three discovery calls, no theme to merge
    # or summarize, and every verbatim counted as processed.
    assert len(llm.prompts) == 3
    row = connection.execute(
        "SELECT processed_count FROM analyses WHERE id = %s", (analysis_id,)
    ).fetchone()
    assert row == (len(CORPUS),)


def test_pipeline_does_nothing_on_an_empty_corpus(
    connection: psycopg.Connection,
) -> None:
    analysis_id = insert_analysis(connection, "running", attempts=1)
    llm = ScriptedLlm([])

    run(connection, analysis_id, llm=llm)

    assert llm.prompts == []
    assert themes_of(connection, analysis_id) == []


def test_pipeline_stops_before_calling_past_the_cost_cap(
    connection: psycopg.Connection,
) -> None:
    analysis_id, _ = seed_running_analysis(connection, CORPUS)
    # Spend already recorded on the analysis (previous attempts included)
    # counts against the cap: one billion input tokens is far past it.
    connection.execute(
        "UPDATE analyses SET input_tokens = 1000000000 WHERE id = %s",
        (analysis_id,),
    )
    connection.commit()
    llm = ScriptedLlm([])

    with pytest.raises(CostCapError, match="cost cap"):
        run(connection, analysis_id, llm=llm)

    assert llm.prompts == []


def test_pipeline_failure_lands_as_a_failed_analysis(
    connection: psycopg.Connection,
) -> None:
    # End to end through process_analysis: the cap becomes a failed analysis
    # with its error visible, never a dead worker.
    analysis_id = insert_analysis(connection, "pending")
    insert_verbatims(connection, analysis_id, CORPUS)
    connection.execute(
        "UPDATE analyses SET input_tokens = 1000000000 WHERE id = %s",
        (analysis_id,),
    )
    connection.commit()

    assert process_analysis(connection, str(analysis_id)) is True

    status, error, _, _ = fetch_analysis(connection, analysis_id)
    assert status == "failed"
    assert error is not None
    assert "cost cap" in error
