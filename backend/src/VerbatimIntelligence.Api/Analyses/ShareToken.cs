namespace VerbatimIntelligence.Api.Analyses;

/// <summary>
/// A public read-only link to an analysis: the raw token travels in the URL
/// and is shown once at creation; this row keeps only its hash. Revocation
/// is the deletion of the row — there is no expiry.
/// </summary>
public sealed class ShareToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid AnalysisId { get; init; }

    /// <summary>SHA-256 of the raw token, lower-case hex (64 chars).</summary>
    public required string TokenHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}