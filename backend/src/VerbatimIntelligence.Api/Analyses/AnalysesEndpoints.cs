using StackExchange.Redis;

using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Analyses;

public static class AnalysesEndpoints
{
    public static IEndpointRouteBuilder MapAnalyses(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/analyses");

        group.MapPost("/", async (
            AppDbContext db,
            IConnectionMultiplexer redis,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            var analysis = new Analysis { CreatedAt = clock.GetUtcNow() };
            db.Analyses.Add(analysis);
            await db.SaveChangesAsync(cancellationToken);

            // Signal after commit: the row is the source of truth. If this
            // push is lost, the analysis stays pending — the reaper will
            // requeue such orphans when it lands (see docs/architecture.md).
            await redis.GetDatabase().ListRightPushAsync(
                RedisKeys.PendingAnalyses, analysis.Id.ToString());

            return Results.Created($"/analyses/{analysis.Id}", AnalysisResponse.From(analysis));
        });

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken cancellationToken) =>
            await db.Analyses.FindAsync([id], cancellationToken) is { } analysis
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