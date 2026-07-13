import json
import os
import re
from dataclasses import dataclass
from typing import Any, Protocol

import anthropic

DEFAULT_MODEL = "claude-opus-4-8"

# Spike lessons, both of them load-bearing: verbatims are referred to by
# numeric id only (fidelity by reference), and the output language is decided
# by the code, never left to the model's appreciation.
SYSTEM = (
    "You analyze raw customer feedback (verbatims). You never quote or rewrite "
    "verbatim text in your outputs: you refer to verbatims strictly by their "
    "numeric id. Themes must emerge from the data, not from a predefined list. "
    "Write every name, description and summary in {language}."
)

MAX_OUTPUT_TOKENS = 16_000

# One call must stay well under the reaper's 5-minute staleness timeout
# (the heartbeat only beats between calls): 90s per try, one retry.
REQUEST_TIMEOUT_SECONDS = 90.0
MAX_RETRIES = 1


@dataclass(frozen=True)
class LlmReply:
    data: dict[str, Any]
    input_tokens: int
    output_tokens: int


class SupportsAsk(Protocol):
    def ask(self, prompt: str, schema: dict[str, Any]) -> LlmReply: ...


def model_from_env() -> str:
    return os.environ.get("ANTHROPIC_MODEL", DEFAULT_MODEL)


def build_llm(language: str) -> SupportsAsk:
    """Build the pipeline's LLM from the environment.

    PIPELINE_LLM=stub swaps the API for the deterministic stub — the dev and
    e2e stacks' equivalent of Mailpit for SMTP. It is opt-in and explicit on
    purpose: production sets nothing and a missing API key fails loudly,
    never silently falls back to stub results.
    """
    if os.environ.get("PIPELINE_LLM") == "stub":
        return StubLlm()
    return AnthropicLlm(model=model_from_env(), language=language)


class AnthropicLlm:
    """Structured-output calls to the Anthropic API.

    Structured outputs guarantee the shape of the reply, never its
    exhaustiveness (spike lesson): callers verify what they receive.
    """

    def __init__(self, model: str, language: str) -> None:
        self._client = anthropic.Anthropic(
            timeout=REQUEST_TIMEOUT_SECONDS, max_retries=MAX_RETRIES
        )
        self._model = model
        self._system = SYSTEM.format(language=language)

    def ask(self, prompt: str, schema: dict[str, Any]) -> LlmReply:
        response = self._client.messages.create(
            model=self._model,
            max_tokens=MAX_OUTPUT_TOKENS,
            system=self._system,
            messages=[{"role": "user", "content": prompt}],
            output_config={"format": {"type": "json_schema", "schema": schema}},
        )
        text = next(block.text for block in response.content if block.type == "text")
        data: dict[str, Any] = json.loads(text)
        return LlmReply(
            data=data,
            input_tokens=response.usage.input_tokens,
            output_tokens=response.usage.output_tokens,
        )


class StubLlm:
    """Deterministic stand-in for the dev and e2e stacks (see build_llm).

    Answers by the shape of the requested schema and only ever selects ids
    that the prompt actually shows — the same contract the pipeline verifies
    on the real model.
    """

    _NUMBERED_LINE = re.compile(r"^\[(\d+)\]", re.MULTILINE)

    def ask(self, prompt: str, schema: dict[str, Any]) -> LlmReply:
        shown_ids = [int(match) for match in self._NUMBERED_LINE.findall(prompt)]
        return LlmReply(
            data=self._reply(shown_ids, schema),
            input_tokens=0,
            output_tokens=0,
        )

    @staticmethod
    def _reply(shown_ids: list[int], schema: dict[str, Any]) -> dict[str, Any]:
        properties = schema["properties"]
        if "summary" in properties:
            return {
                "summary": "Stub synthesis of the theme.",
                "representative_ids": shown_ids[:3],
            }
        item_properties = properties["themes"]["items"]["properties"]
        key = (
            "source_indexes" if "source_indexes" in item_properties else "verbatim_ids"
        )
        return {
            "themes": [
                {
                    "name": "Stub theme",
                    "description": "Deterministic stub output.",
                    key: shown_ids,
                }
            ]
        }
