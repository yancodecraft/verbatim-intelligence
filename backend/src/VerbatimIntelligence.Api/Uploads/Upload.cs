namespace VerbatimIntelligence.Api.Uploads;

/// <summary>
/// A CSV file submitted by an account and stored as-is, awaiting analysis
/// (see docs/glossary.md). It carries its detected columns and data-row count;
/// its verbatims are extracted when an analysis is created against it.
/// </summary>
public sealed class Upload
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>The owning account: every upload belongs to exactly one.</summary>
    public required Guid UserId { get; init; }

    public required string Filename { get; init; }

    /// <summary>The raw file bytes, re-parsed when an analysis is created.</summary>
    public required byte[] Content { get; init; }

    /// <summary>Header names, in file order, as detected at parse time.</summary>
    public required IReadOnlyList<string> Columns { get; init; }

    /// <summary>Number of data rows (excluding the header row).</summary>
    public required int RowCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}