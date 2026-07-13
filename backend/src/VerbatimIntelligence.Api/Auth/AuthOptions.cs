namespace VerbatimIntelligence.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Public base URL of the app, used to build magic links.</summary>
    public required string PublicBaseUrl { get; init; }

    // Off in dev only: the e2e stack serves plain HTTP on a non-localhost
    // hostname, where browsers refuse Secure cookies.
    public bool SecureCookies { get; init; } = true;
}