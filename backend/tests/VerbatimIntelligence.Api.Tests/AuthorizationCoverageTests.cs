using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Auth;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// Architecture guard (docs/security-review.md, F9): every routed endpoint that
/// is not explicitly public must carry the account-scoping marker that
/// <see cref="CurrentAccount.RequireAccount{TBuilder}"/> adds. A new endpoint
/// that forgets RequireAccount fails this test instead of leaking data.
/// </summary>
public sealed class AuthorizationCoverageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Public by design (no session): the sign-in handshake, the token-bearing
    // shared report, and the health probe.
    private static readonly HashSet<string> PublicPaths =
    [
        "auth/magic-link",
        "auth/verify",
        "auth/me",
        "auth/logout",
        "shared/{token}",
        "health",
    ];

    [Fact]
    public void EveryNonPublicEndpointRequiresAnAccount()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var offenders = new List<string>();

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var route = (endpoint.RoutePattern.RawText ?? "").TrimStart('/');
            if (route.StartsWith("openapi", StringComparison.OrdinalIgnoreCase)
                || PublicPaths.Contains(route))
            {
                continue;
            }

            if (endpoint.Metadata.GetMetadata<AccountScopedMarker>() is null)
            {
                offenders.Add(route);
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Non-public endpoints missing RequireAccount(): {string.Join(", ", offenders)}");
    }
}