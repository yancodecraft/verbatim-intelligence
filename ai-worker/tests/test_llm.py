from typing import TYPE_CHECKING

from ai_worker.llm import StubLlm, build_llm
from ai_worker.pipeline import MERGE_SCHEMA, SUMMARY_SCHEMA, THEMES_SCHEMA

if TYPE_CHECKING:
    import pytest

PROMPT = "Some instructions.\n\n[4] too slow\n[7] crashes on save\n[9] love it"


def test_stub_is_opt_in_via_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("PIPELINE_LLM", "stub")
    assert isinstance(build_llm("English"), StubLlm)


def test_stub_discovers_one_theme_over_the_shown_ids() -> None:
    reply = StubLlm().ask(PROMPT, THEMES_SCHEMA)

    assert reply.data == {
        "themes": [
            {
                "name": "Stub theme",
                "description": "Deterministic stub output.",
                "verbatim_ids": [4, 7, 9],
            }
        ]
    }


def test_stub_merges_all_shown_candidates() -> None:
    reply = StubLlm().ask(
        "Candidates:\n[0] A — a (3 verbatims)\n[1] B — b", MERGE_SCHEMA
    )

    assert reply.data["themes"][0]["source_indexes"] == [0, 1]


def test_stub_summarizes_with_shown_ids_only() -> None:
    reply = StubLlm().ask(PROMPT, SUMMARY_SCHEMA)

    assert reply.data["summary"]
    assert reply.data["representative_ids"] == [4, 7, 9]
