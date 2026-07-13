using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Uploads;

public static class UploadsEndpoints
{
    private const int SampleRowLimit = 5;
    private const int FilenameMaxLength = 255;

    public static IEndpointRouteBuilder MapUploads(this IEndpointRouteBuilder routes)
    {
        // DisableAntiforgery: an IFormFile parameter would otherwise require an
        // antiforgery token. The session cookie is SameSite=Lax, so it is not
        // sent on cross-site POSTs — the same CSRF stance as the other writes.
        routes.MapGroup("/uploads").RequireAccount()
            .MapPost("/", UploadAsync)
            .DisableAntiforgery();

        return routes;
    }

    private static async Task<IResult> UploadAsync(
        IFormFile? file,
        HttpContext http,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new UploadError("The file is empty."));
        }

        if (file.Length > CsvContract.MaxBytes)
        {
            return Results.BadRequest(new UploadError("The file is larger than the 5 MB limit."));
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var content = buffer.ToArray();

        switch (CsvContract.Parse(content))
        {
            case CsvParseResult.RejectedCsv rejected:
                return Results.BadRequest(new UploadError(rejected.Message));

            case CsvParseResult.ParsedCsv parsed:
                var upload = new Upload
                {
                    UserId = http.CurrentUser().Id,
                    Filename = SafeFilename(file.FileName),
                    Content = content,
                    Columns = parsed.Headers,
                    RowCount = parsed.Rows.Count,
                    CreatedAt = clock.GetUtcNow(),
                };
                db.Uploads.Add(upload);
                await db.SaveChangesAsync(cancellationToken);

                var sampleRows = parsed.Rows.Take(SampleRowLimit).ToList();
                return Results.Created(
                    $"/uploads/{upload.Id}",
                    new UploadResponse(
                        upload.Id, upload.Filename, parsed.Headers, sampleRows, parsed.Rows.Count));

            default:
                throw new InvalidOperationException("Unhandled CSV parse result.");
        }
    }

    // The filename is untrusted: keep only the leaf, bound its length, escape
    // at display (Vue does by default). A blank name gets a neutral fallback.
    private static string SafeFilename(string? raw)
    {
        var name = Path.GetFileName(raw ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            name = "upload.csv";
        }

        return name.Length > FilenameMaxLength ? name[..FilenameMaxLength] : name;
    }
}

public sealed record UploadResponse(
    Guid Id,
    string Filename,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> SampleRows,
    int RowCount);

public sealed record UploadError(string Message);