using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using VerbatimIntelligence.Api.Data;
using VerbatimIntelligence.Api.Email;

namespace VerbatimIntelligence.Api.Auth;

public static class AuthEndpoints
{
    public const string SessionCookieName = "vi_session";

    private static readonly TimeSpan LoginTokenLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    // Rate limits live in the database (login_tokens is the ledger): they
    // hold across restarts and replicas, and need no client IP — the
    // per-address cap is what stops mailbox bombing, the global cap protects
    // the TEM quota. See docs/practices.md ("rate limiting" on magic links).
    private const int MaxLinksPerAddress = 5;
    private static readonly TimeSpan PerAddressWindow = TimeSpan.FromMinutes(15);
    private const int MaxLinksGlobal = 30;
    private static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(5);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        // "/auth", not "/api/auth": both proxies (Vite dev, Caddy prod) strip
        // the /api prefix before forwarding — same contract as /analyses.
        var group = app.MapGroup("/auth");

        group.MapPost("/magic-link", RequestMagicLinkAsync);
        group.MapPost("/verify", VerifyAsync);
        group.MapGet("/me", MeAsync);
        group.MapPost("/logout", LogoutAsync);
        group.MapDelete("/account", DeleteAccountAsync).RequireAccount();
    }

    /// <summary>
    /// Resolves the account behind the request's session cookie, or null.
    /// The single place where a cookie becomes an account.
    /// </summary>
    internal static async Task<User?> ResolveUserAsync(
        HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (http.Request.Cookies[SessionCookieName] is not { Length: > 0 } raw)
        {
            return null;
        }

        var hash = Tokens.Hash(raw);
        var now = clock.GetUtcNow();
        return await db.Sessions
            .Where(session => session.TokenHash == hash && session.ExpiresAt > now)
            .Join(db.Users, session => session.UserId, user => user.Id, (_, user) => user)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static async Task<IResult> RequestMagicLinkAsync(
        MagicLinkRequest request,
        AppDbContext db,
        IEmailSender emailSender,
        IOptions<AuthOptions> authOptions,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (email.Length is 0 or > 320 || !email.Contains('@', StringComparison.Ordinal))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["A valid e-mail address is required."],
            });
        }

        var now = clock.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == email, cancellationToken);
        if (user is null)
        {
            // The account is born on first sign-in attempt (see glossary).
            user = new User { Email = email, CreatedAt = now };
            db.Users.Add(user);
        }

        var addressWindowStart = now.Subtract(PerAddressWindow);
        var globalWindowStart = now.Subtract(GlobalWindow);
        var recentForAddress = await db.LoginTokens.CountAsync(
            token => token.UserId == user.Id && token.CreatedAt > addressWindowStart,
            cancellationToken);
        var recentGlobal = await db.LoginTokens.CountAsync(
            token => token.CreatedAt > globalWindowStart, cancellationToken);
        if (recentForAddress >= MaxLinksPerAddress || recentGlobal >= MaxLinksGlobal)
        {
            return TypedResults.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var rawToken = Tokens.CreateRaw();
        db.LoginTokens.Add(new LoginToken
        {
            UserId = user.Id,
            TokenHash = Tokens.Hash(rawToken),
            CreatedAt = now,
            ExpiresAt = now.Add(LoginTokenLifetime),
        });
        await db.SaveChangesAsync(cancellationToken);

        var link = $"{authOptions.Value.PublicBaseUrl}/verify?token={rawToken}";
        await emailSender.SendAsync(
            email,
            "Sign in to Verbatim Intelligence",
            $"Open this link to sign in:\n\n{link}\n\n"
            + "It expires in 15 minutes and works once. "
            + "If you didn't request it, you can ignore this e-mail.",
            cancellationToken);

        // Always 202, whether the account existed or not: the response must
        // not reveal which e-mail addresses have an account here.
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> VerifyAsync(
        VerifyRequest request,
        HttpContext http,
        AppDbContext db,
        IOptions<AuthOptions> authOptions,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Token))
        {
            return TypedResults.Unauthorized();
        }

        var hash = Tokens.Hash(request.Token);
        var now = clock.GetUtcNow();
        var loginToken = await db.LoginTokens.SingleOrDefaultAsync(
            candidate => candidate.TokenHash == hash
                && candidate.UsedAt == null
                && candidate.ExpiresAt > now,
            cancellationToken);
        if (loginToken is null)
        {
            return TypedResults.Unauthorized();
        }

        loginToken.MarkUsed(now);
        var rawSession = Tokens.CreateRaw();
        db.Sessions.Add(new Session
        {
            UserId = loginToken.UserId,
            TokenHash = Tokens.Hash(rawSession),
            CreatedAt = now,
            ExpiresAt = now.Add(SessionLifetime),
        });
        await db.SaveChangesAsync(cancellationToken);

        // S2092: Secure is on wherever TLS exists (production); dev and e2e
        // serve plain HTTP on non-localhost hosts, where browsers would
        // silently drop a Secure cookie.
#pragma warning disable S2092
        http.Response.Cookies.Append(SessionCookieName, rawSession, new CookieOptions
        {
            HttpOnly = true,
            Secure = authOptions.Value.SecureCookies,
            SameSite = SameSiteMode.Lax,
            MaxAge = SessionLifetime,
            Path = "/",
        });
#pragma warning restore S2092
        return TypedResults.NoContent();
    }

    private static async Task<IResult> MeAsync(
        HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(http, db, clock, cancellationToken);
        return user is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new MeResponse(user.Email));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http, AppDbContext db, CancellationToken cancellationToken)
    {
        if (http.Request.Cookies[SessionCookieName] is { Length: > 0 } raw)
        {
            var hash = Tokens.Hash(raw);
            await db.Sessions
                .Where(session => session.TokenHash == hash)
                .ExecuteDeleteAsync(cancellationToken);
            http.Response.Cookies.Delete(SessionCookieName);
        }

        return TypedResults.NoContent();
    }

    // Right to erasure for the whole account: deleting the user cascades in
    // the database to its analyses (and their verbatims, themes and share
    // tokens), uploads, login tokens and sessions — its entire footprint
    // (docs/security-review.md, B1). The session cookie is cleared too.
    private static async Task<IResult> DeleteAccountAsync(
        HttpContext http, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = http.CurrentUser().Id;
        await db.Users
            .Where(user => user.Id == userId)
            .ExecuteDeleteAsync(cancellationToken);
        http.Response.Cookies.Delete(SessionCookieName);
        return TypedResults.NoContent();
    }

    private sealed record MagicLinkRequest(string? Email);

    private sealed record VerifyRequest(string? Token);

    private sealed record MeResponse(string Email);
}