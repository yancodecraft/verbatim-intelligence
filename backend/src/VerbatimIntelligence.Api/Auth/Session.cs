namespace VerbatimIntelligence.Api.Auth;

/// <summary>
/// An established sign-in: an opaque token in an httpOnly cookie, backed by
/// this row (hashed, like login tokens) — hence revocable server-side.
/// </summary>
public sealed class Session
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    /// <summary>SHA-256 of the raw token, lower-case hex (64 chars).</summary>
    public required string TokenHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}