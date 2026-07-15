using Microsoft.EntityFrameworkCore;

using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Auth;

/// <summary>
/// Periodic hygiene of the auth tables (docs/security-review.md, D5/A4):
/// expired login tokens and sessions are dropped, and accounts that were never
/// really used — no session, no analysis, no upload, no live token, older than
/// the grace period — are removed, so an e-mail is not retained forever after a
/// sign-in link that went nowhere. The predicate is deliberately strict: an
/// account holding any data is never touched.
/// </summary>
public static class AuthCleanup
{
    public static readonly TimeSpan UnusedAccountGrace = TimeSpan.FromDays(30);

    public static async Task RunAsync(
        AppDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await db.LoginTokens
            .Where(token => token.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Sessions
            .Where(session => session.ExpiresAt < now)
            .ExecuteDeleteAsync(cancellationToken);

        // Runs unscoped (no current account), so the global query filters are
        // open and these counts see every row. Expired tokens are already gone
        // above, so a remaining login_token means a live sign-in in progress.
        var cutoff = now - UnusedAccountGrace;
        await db.Users
            .Where(user => user.CreatedAt < cutoff
                && !db.Sessions.Any(session => session.UserId == user.Id)
                && !db.LoginTokens.Any(token => token.UserId == user.Id)
                && !db.Analyses.Any(analysis => analysis.UserId == user.Id)
                && !db.Uploads.Any(upload => upload.UserId == user.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}