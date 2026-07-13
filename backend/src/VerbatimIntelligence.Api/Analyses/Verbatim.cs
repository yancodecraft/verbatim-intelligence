namespace VerbatimIntelligence.Api.Analyses;

/// <summary>
/// One raw customer feedback, word for word (see docs/glossary.md). Part of
/// the Analysis aggregate: verbatims are extracted from an upload when the
/// analysis is created and never reworded afterwards — the pipeline cites them
/// by reference (id), which is what makes fidelity a tested invariant.
/// </summary>
public sealed class Verbatim
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid AnalysisId { get; init; }

    /// <summary>0-based index of the source row in the file (data rows only).</summary>
    public required int Position { get; init; }

    public required string Text { get; init; }
}