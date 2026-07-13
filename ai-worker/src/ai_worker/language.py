import re

# Spike lesson: left to "the dominant language of the corpus", the model
# answered in French on a 95% English corpus. The system decides the output
# language and tells the LLM. A stopword heuristic is enough for V1: it is
# deterministic, dependency-free and testable; unsure means English.
DEFAULT_LANGUAGE = "English"

_WORDS = re.compile(r"[^\W\d_]+")

_SAMPLE_SIZE = 200

# Below this many stopword hits the corpus is too short or too alien to
# call: default rather than guess.
_MIN_HITS = 5

_STOPWORDS: dict[str, frozenset[str]] = {
    "English": frozenset(
        [
            "the",
            "and",
            "is",
            "are",
            "to",
            "of",
            "it",
            "this",
            "that",
            "for",
            "with",
            "not",
            "was",
            "but",
            "have",
        ]
    ),
    "French": frozenset(
        [
            "le",
            "la",
            "les",
            "et",
            "est",
            "de",
            "des",
            "un",
            "une",
            "pas",
            "pour",
            "que",
            "qui",
            "dans",
            "ce",
            "ne",
            "je",
        ]
    ),
    "Spanish": frozenset(
        [
            "el",
            "los",
            "las",
            "es",
            "de",
            "un",
            "una",
            "que",
            "para",
            "con",
            "no",
            "por",
            "se",
            "como",
            "más",
        ]
    ),
    "German": frozenset(
        [
            "der",
            "die",
            "das",
            "und",
            "ist",
            "nicht",
            "ein",
            "eine",
            "mit",
            "für",
            "auf",
            "zu",
            "ich",
            "es",
        ]
    ),
    "Italian": frozenset(
        [
            "il",
            "gli",
            "le",
            "e",
            "è",
            "di",
            "un",
            "una",
            "che",
            "per",
            "con",
            "non",
            "si",
            "come",
            "anche",
        ]
    ),
    "Portuguese": frozenset(
        [
            "o",
            "os",
            "as",
            "é",
            "de",
            "um",
            "uma",
            "que",
            "para",
            "com",
            "não",
            "se",
            "como",
            "mais",
            "foi",
        ]
    ),
}


def detect_language(texts: list[str]) -> str:
    """Return the dominant language of the corpus, as an English word."""
    words = [
        word.casefold()
        for text in texts[:_SAMPLE_SIZE]
        for word in _WORDS.findall(text)
    ]
    if not words:
        return DEFAULT_LANGUAGE
    scores = {
        language: sum(1 for word in words if word in stopwords)
        for language, stopwords in _STOPWORDS.items()
    }
    best = max(scores, key=lambda language: scores[language])
    if scores[best] < _MIN_HITS:
        return DEFAULT_LANGUAGE
    return best
