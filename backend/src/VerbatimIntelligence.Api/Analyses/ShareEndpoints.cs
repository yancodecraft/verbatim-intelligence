using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Analyses;

public static class ShareEndpoints
{
    public const string RateLimitPolicy = "shared-report";

    public static IEndpointRouteBuilder MapShares(this IEndpointRouteBuilder routes)
    {
        // Owner side, scoped like every /analyses endpoint: another
        // account's analysis is a plain 404 through the global query filter.
        var owner = routes.MapGroup("/analyses/{id:guid}/share").RequireAccount();

        owner.MapPost("", async (
            Guid id,
            AppDbContext db,
            IOptions<AuthOptions> authOptions,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            var analysis = await db.Analyses.SingleOrDefaultAsync(
                candidate => candidate.Id == id, cancellationToken);
            if (analysis is null)
            {
                return Results.NotFound();
            }

            if (analysis.Status != AnalysisStatus.Succeeded)
            {
                return Results.BadRequest(
                    new { message = "Only a succeeded analysis can be shared." });
            }

            // One active link per analysis: creating replaces any previous
            // link (the raw is only shown once — regeneration is the way to
            // get a fresh one, and it revokes the old link by construction).
            // The replacement is serialized on the analysis row: without the
            // lock, two concurrent creations would both pass the delete and
            // collide on the unique analysis_id index — a bare 500.
            await using var transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlAsync(
                $"SELECT id FROM analyses WHERE id = {analysis.Id} FOR UPDATE",
                cancellationToken);
            await db.ShareTokens
                .Where(token => token.AnalysisId == analysis.Id)
                .ExecuteDeleteAsync(cancellationToken);

            // The raw token leaves in this response and is never stored or
            // logged; the row keeps its hash only.
            var raw = Tokens.CreateRaw();
            db.ShareTokens.Add(new ShareToken
            {
                AnalysisId = analysis.Id,
                TokenHash = Tokens.Hash(raw),
                CreatedAt = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var url = $"{authOptions.Value.PublicBaseUrl}/shared/{raw}";
            return Results.Ok(new ShareResponse(url));
        });

        owner.MapDelete("", async (
            Guid id, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var analysis = await db.Analyses.SingleOrDefaultAsync(
                candidate => candidate.Id == id, cancellationToken);
            if (analysis is null)
            {
                return Results.NotFound();
            }

            // Revocation is the deletion of the row; already-revoked is not
            // an error, the outcome is the same.
            await db.ShareTokens
                .Where(token => token.AnalysisId == analysis.Id)
                .ExecuteDeleteAsync(cancellationToken);
            return Results.NoContent();
        });

        // Public side: no account, no session. The query filters stay open
        // (AppDbContext) and the token is the only access capability — it
        // designates exactly one analysis. Unknown and revoked tokens are
        // the same bare 404.
        var shared = routes.MapGroup("/shared").RequireRateLimiting(RateLimitPolicy);

        shared.MapGet("/{token}", async (
            string token, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var hash = Tokens.Hash(token);
            var shareToken = await db.ShareTokens.SingleOrDefaultAsync(
                candidate => candidate.TokenHash == hash, cancellationToken);
            if (shareToken is null)
            {
                return Results.NotFound();
            }

            var analysis = await db.Analyses.SingleAsync(
                candidate => candidate.Id == shareToken.AnalysisId, cancellationToken);
            var themes = await AnalysisReadModel.ThemesAsync(
                db, analysis.Id, cancellationToken);
            var unclassifiedCount = await AnalysisReadModel.UnclassifiedCountAsync(
                db, analysis.Id, cancellationToken);

            // The link may be revoked at any time: intermediaries and
            // browsers must not keep a copy that outlives it.
            return Results.Ok(SharedReportResponse.From(analysis, unclassifiedCount, themes));
        }).AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return result;
        });

        return routes;
    }
}

public sealed record ShareResponse(string Url);

/// <summary>
/// The public report: content only. No internal id, no pipeline telemetry
/// (status, progress, error) — a shared link shows what the analysis found,
/// nothing about how the system ran it.
/// </summary>
public sealed record SharedReportResponse(
    string SourceFilename,
    DateTimeOffset CreatedAt,
    int VerbatimCount,
    int UnclassifiedCount,
    IReadOnlyList<ThemeResponse> Themes)
{
    public static SharedReportResponse From(
        Analysis analysis, int unclassifiedCount, IReadOnlyList<ThemeResponse> themes) =>
        new(
            analysis.SourceFilename,
            analysis.CreatedAt,
            analysis.VerbatimCount,
            unclassifiedCount,
            themes);
}