namespace VerbatimIntelligence.Api.Analyses;

/// <summary>
/// An emergent theme discovered in the corpus itself (see docs/glossary.md).
/// Written by the worker at the end of the pipeline; part of the Analysis
/// aggregate, only ever reached through its (already scoped) analysis.
/// </summary>
public sealed class Theme
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid AnalysisId { get; init; }

    public required string Name { get; init; }

    /// <summary>The faithful summary of the theme, in the corpus language.</summary>
    public required string Synthesis { get; init; }

    /// <summary>0-based display order within the analysis, decided by the worker.</summary>
    public required int Position { get; init; }
}