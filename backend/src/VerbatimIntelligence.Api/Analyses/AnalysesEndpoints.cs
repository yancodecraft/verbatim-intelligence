using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Analyses;

public static class AnalysesEndpoints
{
    public static IEndpointRouteBuilder MapAnalyses(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/analyses");

        group.MapPost("/", async (AppDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var analysis = new Analysis { CreatedAt = clock.GetUtcNow() };
            db.Analyses.Add(analysis);
            await db.SaveChangesAsync(cancellationToken);
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