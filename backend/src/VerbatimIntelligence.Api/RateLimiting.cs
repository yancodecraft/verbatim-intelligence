namespace VerbatimIntelligence.Api;

/// <summary>
/// Rate-limit partitioning by client. <c>UseForwardedHeaders</c> rewrites
/// <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/> to
/// the real caller (the backend has no published port, so the header cannot be
/// spoofed from outside the compose network), which lets each window be scoped
/// per client: one caller can no longer saturate a global window for everyone
/// (docs/security-review.md, O1/O2).
/// </summary>
public static class RateLimiting
{
    public static string ClientPartitionKey(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}