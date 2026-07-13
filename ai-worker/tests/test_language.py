from ai_worker.language import detect_language

FRENCH = [
    "Le produit est vraiment lent depuis la mise à jour.",
    "Je ne comprends pas pourquoi l'export ne fonctionne plus.",
    "C'est une bonne idée mais il manque des fonctionnalités pour les équipes.",
]

ENGLISH = [
    "The product is really slow since the update.",
    "I don't understand why the export is broken.",
    "It is a good idea but it lacks features for the teams.",
]


def test_detects_french() -> None:
    assert detect_language(FRENCH) == "French"


def test_detects_english() -> None:
    assert detect_language(ENGLISH) == "English"


def test_defaults_to_english_when_nothing_matches() -> None:
    assert detect_language(["zzz qqq 12345", "###"]) == "English"


def test_defaults_to_english_on_an_empty_corpus() -> None:
    assert detect_language([]) == "English"
