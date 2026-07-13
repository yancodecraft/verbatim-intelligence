namespace VerbatimIntelligence.Api.Analyses;

/// <summary>
/// Attaches a verbatim to a theme (see docs/glossary.md). The reference IS
/// the fidelity invariant: a cited verbatim is a foreign key to the original
/// row, never regenerated text — the LLM selects ids, this table stores them.
/// </summary>
public sealed class ThemeVerbatim
{
    public required Guid ThemeId { get; init; }

    public required Guid VerbatimId { get; init; }

    /// <summary>
    /// Citation order among the representative verbatims of the theme
    /// (0-based); null for verbatims that support the theme without being
    /// cited by its synthesis.
    /// </summary>
    public int? Rank { get; init; }
}