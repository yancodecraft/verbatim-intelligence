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

    /// <summary>The owning account: every analysis belongs to exactly one.</summary>
    public required Guid UserId { get; init; }

    public AnalysisStatus Status { get; private set; } = AnalysisStatus.Pending;

    /// <summary>Denormalized from the source upload, for the analyses list.</summary>
    public string SourceFilename { get; init; } = "";

    /// <summary>Number of verbatims extracted into this analysis.</summary>
    public int VerbatimCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    // The pipeline columns below are written by the worker through raw SQL
    // (heartbeat, retries, progress, spend — see docs/architecture.md,
    // "Résilience du traitement asynchrone"); the backend only reads them.

    /// <summary>Last sign of life: beaten by the processing worker, stamped by the reaper on requeue/republish.</summary>
    public DateTimeOffset? HeartbeatAt { get; init; }

    /// <summary>Times a worker claimed this analysis; the reaper fails it past a limit.</summary>
    public int Attempts { get; init; }

    /// <summary>Why the analysis failed, when it did — shown to the user.</summary>
    public string? Error { get; init; }

    /// <summary>Verbatims already through theme discovery, for progress display.</summary>
    public int ProcessedCount { get; init; }

    /// <summary>LLM tokens consumed so far, backing the per-analysis cost cap.</summary>
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }
}