using Microsoft.EntityFrameworkCore;

using StackExchange.Redis;

using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Analyses;

public static class AnalysesEndpoints
{
    public static IEndpointRouteBuilder MapAnalyses(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/analyses").RequireAccount();

        group.MapPost("/", async (
            HttpContext http,
            AppDbContext db,
            IConnectionMultiplexer redis,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            var analysis = new Analysis
            {
                UserId = http.CurrentUser().Id,
                CreatedAt = clock.GetUtcNow(),
            };
            db.Analyses.Add(analysis);
            await db.SaveChangesAsync(cancellationToken);

            // Signal after commit: the row is the source of truth. If this
            // push is lost, the analysis stays pending — the reaper will
            // requeue such orphans when it lands (see docs/architecture.md).
            await redis.GetDatabase().ListRightPushAsync(
                RedisKeys.PendingAnalyses, analysis.Id.ToString());

            return Results.Created($"/analyses/{analysis.Id}", AnalysisResponse.From(analysis));
        });

        // Another account's analysis is a plain 404: the response must not
        // reveal that the id exists at all.
        group.MapGet("/{id:guid}", async (
            Guid id, HttpContext http, AppDbContext db, CancellationToken cancellationToken) =>
            await db.Analyses.SingleOrDefaultAsync(
                analysis => analysis.Id == id && analysis.UserId == http.CurrentUser().Id,
                cancellationToken) is { } analysis
                ? Results.Ok(AnalysisResponse.From(analysis))
                : Results.NotFound());

        return routes;
    }
}

public sealed record AnalysisResponse(Guid Id, AnalysisStatus Status, DateTimeOffset CreatedAt)
{
    public static AnalysisResponse From(Analysis analysis) =>
        new(analysis.Id, analysis.Status, analysis.CreatedAt);
}