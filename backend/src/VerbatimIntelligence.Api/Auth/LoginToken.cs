namespace VerbatimIntelligence.Api.Auth;

/// <summary>
/// The server side of a magic link: only the SHA-256 of the token is stored,
/// so a database leak never leaks a usable link. Single-use, short-lived.
/// </summary>
public sealed class LoginToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    /// <summary>SHA-256 of the raw token, lower-case hex (64 chars).</summary>
    public required string TokenHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; private set; }

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}