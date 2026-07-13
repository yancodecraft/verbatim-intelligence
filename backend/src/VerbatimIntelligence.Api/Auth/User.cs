namespace VerbatimIntelligence.Api.Auth;

/// <summary>
/// An account: an identity reduced to its e-mail address, created on first
/// sign-in (see docs/glossary.md). Every piece of data belongs to exactly
/// one account.
/// </summary>
public sealed class User
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Normalized (trimmed, lower-cased) before it reaches here.</summary>
    public required string Email { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}