using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Auth;

/// <summary>
/// The single mechanical scoping point (see docs/roadmap.md, slice 2): every
/// account-scoped endpoint group takes this filter, and handlers read the
/// resolved account — they can't forget to check.
/// </summary>
public static class CurrentAccount
{
    private const string ItemKey = "vi:current-user";

    public static TBuilder RequireAccount<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        // A marker on the endpoint so an architecture test can prove every
        // non-public endpoint is account-scoped (docs/security-review.md, F9).
        builder.Add(endpoint => endpoint.Metadata.Add(AccountScopedMarker.Instance));
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var user = await AuthEndpoints.ResolveUserAsync(
                http,
                http.RequestServices.GetRequiredService<AppDbContext>(),
                http.RequestServices.GetRequiredService<TimeProvider>(),
                http.RequestAborted);
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            http.Items[ItemKey] = user;
            // Arms the global query filters: from here on, account-scoped
            // entities are invisible unless they belong to this account.
            http.RequestServices.GetRequiredService<CurrentAccountAccessor>().UserId = user.Id;
            return await next(context);
        });
    }

    /// <summary>Only callable behind <see cref="RequireAccount{TBuilder}"/>.</summary>
    public static User CurrentUser(this HttpContext http) =>
        http.Items[ItemKey] as User
        ?? throw new InvalidOperationException(
            "No account on this request: the endpoint is missing RequireAccount().");
}

/// <summary>Endpoint metadata marking an endpoint as account-scoped.</summary>
public sealed class AccountScopedMarker
{
    public static readonly AccountScopedMarker Instance = new();

    private AccountScopedMarker()
    {
    }
}