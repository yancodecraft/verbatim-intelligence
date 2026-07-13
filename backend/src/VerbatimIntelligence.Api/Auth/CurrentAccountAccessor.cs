namespace VerbatimIntelligence.Api.Auth;

/// <summary>
/// Ambient account of the current request, set once by RequireAccount and
/// read by the AppDbContext global query filters. Null means no account was
/// resolved: system paths (migrations, health, schema tests) — endpoints
/// behind RequireAccount always see it set.
/// </summary>
public sealed class CurrentAccountAccessor
{
    public Guid? UserId { get; set; }
}