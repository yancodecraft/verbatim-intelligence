namespace VerbatimIntelligence.Api.Analyses;

public enum AnalysisStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// The asynchronous unit of work of the system: its id transits through the
/// queue, its state lives in the database (see docs/glossary.md).
/// </summary>
public sealed class Analysis
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public AnalysisStatus Status { get; private set; } = AnalysisStatus.Pending;

    public required DateTimeOffset CreatedAt { get; init; }
}