using Microsoft.EntityFrameworkCore;

using StackExchange.Redis;

using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;
using VerbatimIntelligence.Api.Uploads;

namespace VerbatimIntelligence.Api.Analyses;

public static class AnalysesEndpoints
{
    public static IEndpointRouteBuilder MapAnalyses(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/analyses").RequireAccount();

        group.MapPost("/", async (
            CreateAnalysisRequest? request,
            HttpContext http,
            AppDbContext db,
            IConnectionMultiplexer redis,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            if (request?.UploadId is not { } uploadId || string.IsNullOrEmpty(request.VerbatimColumn))
            {
                return Results.BadRequest(
                    new { message = "An upload id and a verbatim column are required." });
            }

            // The query filter scopes uploads to the caller: another account's
            // upload is simply not found — a plain 404, like an unknown id.
            var upload = await db.Uploads.SingleOrDefaultAsync(
                candidate => candidate.Id == uploadId, cancellationToken);
            if (upload is null)
            {
                return Results.NotFound();
            }

            var columnIndex = upload.Columns.ToList().IndexOf(request.VerbatimColumn);
            if (columnIndex < 0)
            {
                return Results.BadRequest(new
                {
                    message = $"Column \"{request.VerbatimColumn}\" does not exist in this upload.",
                });
            }

            // Re-parse the stored file to read the chosen column. It parsed
            // cleanly at upload, so a rejection here would be a bug, not input.
            if (CsvContract.Parse(upload.Content) is not CsvParseResult.ParsedCsv parsed)
            {
                return Results.BadRequest(new { message = "The stored upload could no longer be read." });
            }

            // Verbatims reference the analysis, so its id is fixed up front;
            // its verbatim count is known only once the column is extracted.
            var analysisId = Guid.CreateVersion7();
            var verbatims = ExtractVerbatims(analysisId, parsed.Rows, columnIndex);

            var analysis = new Analysis
            {
                Id = analysisId,
                UserId = http.CurrentUser().Id,
                CreatedAt = clock.GetUtcNow(),
                SourceFilename = upload.Filename,
                VerbatimCount = verbatims.Count,
            };
            db.Analyses.Add(analysis);
            db.Verbatims.AddRange(verbatims);
            await db.SaveChangesAsync(cancellationToken);

            // Signal after commit: the row is the source of truth. If this
            // push is lost, the analysis stays pending — the reaper will
            // requeue such orphans when it lands (see docs/architecture.md).
            await redis.GetDatabase().ListRightPushAsync(
                RedisKeys.PendingAnalyses, analysis.Id.ToString());

            return Results.Created($"/analyses/{analysis.Id}", AnalysisResponse.From(analysis));
        });

        // The account's analyses, newest first. The global query filter
        // (AppDbContext) scopes the set to the caller — another account's
        // analyses are simply not there.
        group.MapGet("/", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var analyses = await db.Analyses
                .OrderByDescending(analysis => analysis.CreatedAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(analyses.Select(AnalysisResponse.From));
        });

        // Another account's analysis is a plain 404, indistinguishable from
        // a non-existent id: the global query filter (AppDbContext) hides it
        // before this query even looks.
        group.MapGet("/{id:guid}", async (
            Guid id, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var analysis = await db.Analyses.SingleOrDefaultAsync(
                candidate => candidate.Id == id, cancellationToken);
            if (analysis is null)
            {
                return Results.NotFound();
            }

            // Themes and citations are read through the (already scoped)
            // analysis, via the projection shared with the public report.
            var themes = await AnalysisReadModel.ThemesAsync(
                db, analysis.Id, cancellationToken);
            var unclassifiedCount = await AnalysisReadModel.UnclassifiedCountAsync(
                db, analysis.Id, cancellationToken);

            return Results.Ok(AnalysisDetailResponse.From(analysis, unclassifiedCount, themes));
        });

        return routes;
    }

    private static List<Verbatim> ExtractVerbatims(
        Guid analysisId, IReadOnlyList<IReadOnlyList<string>> rows, int columnIndex)
    {
        var verbatims = new List<Verbatim>();
        for (var position = 0; position < rows.Count; position++)
        {
            var row = rows[position];
            var cell = columnIndex < row.Count ? row[columnIndex] : "";

            // Empty cells are not verbatims: skipped, never stored. File order
            // is kept, so a skipped row leaves a gap in the positions — each
            // stored verbatim still points at its source line.
            if (string.IsNullOrWhiteSpace(cell))
            {
                continue;
            }

            verbatims.Add(new Verbatim { AnalysisId = analysisId, Position = position, Text = cell });
        }

        return verbatims;
    }
}

public sealed record CreateAnalysisRequest(Guid? UploadId, string? VerbatimColumn);

public sealed record AnalysisResponse(
    Guid Id,
    AnalysisStatus Status,
    DateTimeOffset CreatedAt,
    string SourceFilename,
    int VerbatimCount,
    int ProcessedCount,
    string? Error)
{
    public static AnalysisResponse From(Analysis analysis) =>
        new(
            analysis.Id,
            analysis.Status,
            analysis.CreatedAt,
            analysis.SourceFilename,
            analysis.VerbatimCount,
            analysis.ProcessedCount,
            analysis.Error);
}

public sealed record RepresentativeResponse(int Position, string Text);

public sealed record ThemeResponse(
    string Name,
    string Synthesis,
    int VerbatimCount,
    IReadOnlyList<RepresentativeResponse> Representatives);

public sealed record AnalysisDetailResponse(
    Guid Id,
    AnalysisStatus Status,
    DateTimeOffset CreatedAt,
    string SourceFilename,
    int VerbatimCount,
    int ProcessedCount,
    string? Error,
    int UnclassifiedCount,
    IReadOnlyList<ThemeResponse> Themes)
{
    public static AnalysisDetailResponse From(
        Analysis analysis, int unclassifiedCount, IReadOnlyList<ThemeResponse> themes) =>
        new(
            analysis.Id,
            analysis.Status,
            analysis.CreatedAt,
            analysis.SourceFilename,
            analysis.VerbatimCount,
            analysis.ProcessedCount,
            analysis.Error,
            unclassifiedCount,
            themes);
}