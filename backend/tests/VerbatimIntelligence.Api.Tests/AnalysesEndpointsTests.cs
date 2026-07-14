using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// An analysis is created from an uploaded CSV and a chosen verbatim column,
/// scoped to the account. It extracts the column into verbatims (empty cells
/// skipped, file order kept) and enqueues the analysis for the worker.
/// </summary>
public class AnalysesEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string DefaultCsv = "comment,score\nGreat product,9\nToo slow,3\n";

    private sealed record AnalysisResponse(
        Guid Id, string Status, DateTimeOffset CreatedAt, string SourceFilename, int VerbatimCount);

    private sealed record RepresentativeResponse(int Position, string Text);

    private sealed record ThemeResponse(
        string Name, string Synthesis, int VerbatimCount, List<RepresentativeResponse> Representatives);

    private sealed record AnalysisDetailResponse(
        Guid Id,
        string Status,
        int VerbatimCount,
        int ProcessedCount,
        string? Error,
        int UnclassifiedCount,
        List<ThemeResponse> Themes);

    private sealed record UploadResult(Guid Id, List<string> Columns);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        await SignInFlow.SignInAsync(
            client, factory.MailpitApiBaseAddress, $"{Guid.NewGuid():N}@example.test");
        return client;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string csv, string filename = "feedback.csv")
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var response = await client.PostAsync(
            "/uploads", new MultipartFormDataContent { { file, "file", filename } });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<UploadResult>();
        Assert.NotNull(body);
        return body.Id;
    }

    private static async Task<AnalysisResponse> CreateAnalysisAsync(
        HttpClient client, string verbatimColumn = "comment", string csv = DefaultCsv)
    {
        var uploadId = await UploadAsync(client, csv);
        var response = await client.PostAsJsonAsync(
            "/analyses", new { uploadId, verbatimColumn });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(body);
        return body;
    }

    [Fact]
    public async Task PostAnalyses_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/analyses", new { uploadId = Guid.NewGuid(), verbatimColumn = "comment" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalysis_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient().GetAsync($"/analyses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyses_CreatesPendingAnalysisFromTheUpload()
    {
        var client = await SignedInClientAsync();

        var analysis = await CreateAnalysisAsync(client);

        Assert.NotEqual(Guid.Empty, analysis.Id);
        Assert.Equal("pending", analysis.Status);
        Assert.Equal("feedback.csv", analysis.SourceFilename);
        Assert.Equal(2, analysis.VerbatimCount);
    }

    [Fact]
    public async Task PostAnalyses_WithUnknownUpload_Returns404()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync(
            "/analyses", new { uploadId = Guid.NewGuid(), verbatimColumn = "comment" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyses_WithAnotherAccountsUpload_Returns404()
    {
        var owner = await SignedInClientAsync();
        var uploadId = await UploadAsync(owner, DefaultCsv);

        var intruder = await SignedInClientAsync();
        var response = await intruder.PostAsJsonAsync(
            "/analyses", new { uploadId, verbatimColumn = "comment" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyses_WithAColumnThatDoesNotExist_Returns400()
    {
        var client = await SignedInClientAsync();
        var uploadId = await UploadAsync(client, DefaultCsv);

        var response = await client.PostAsJsonAsync(
            "/analyses", new { uploadId, verbatimColumn = "nonexistent" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyses_ExtractsColumnIntoVerbatims_SkippingEmptyCells()
    {
        var client = await SignedInClientAsync();

        // Row at file position 1 has an empty comment: skipped, not counted.
        var analysis = await CreateAnalysisAsync(
            client, "comment", "comment,score\nfirst,1\n,2\nthird,3\n");

        Assert.Equal(2, analysis.VerbatimCount);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verbatims = await db.Verbatims
            .Where(verbatim => verbatim.AnalysisId == analysis.Id)
            .OrderBy(verbatim => verbatim.Position)
            .ToListAsync();

        Assert.Equal(2, verbatims.Count);
        Assert.Equal(0, verbatims[0].Position);
        Assert.Equal("first", verbatims[0].Text);
        // File order is preserved: the skipped empty leaves a gap at position 1.
        Assert.Equal(2, verbatims[1].Position);
        Assert.Equal("third", verbatims[1].Text);
    }

    [Fact]
    public async Task GetAnalysis_ReturnsCreatedAnalysis()
    {
        var client = await SignedInClientAsync();
        var created = await CreateAnalysisAsync(client);

        var response = await client.GetAsync($"/analyses/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var analysis = await response.Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(analysis);
        Assert.Equal(created.Id, analysis.Id);
        Assert.Equal("pending", analysis.Status);
    }

    [Fact]
    public async Task GetAnalysis_OfAnotherAccount_Returns404()
    {
        var owner = await SignedInClientAsync();
        var created = await CreateAnalysisAsync(owner);

        var intruder = await SignedInClientAsync();
        var response = await intruder.GetAsync($"/analyses/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyses_EnqueuesAnalysisId()
    {
        var client = await SignedInClientAsync();

        var created = await CreateAnalysisAsync(client);

        await using var redis = await StackExchange.Redis.ConnectionMultiplexer
            .ConnectAsync(factory.RedisConnectionString);
        var queued = await redis.GetDatabase()
            .ListRangeAsync(Analyses.RedisKeys.PendingAnalyses);
        Assert.Contains(created.Id.ToString(), queued.Select(value => (string?)value));
    }

    [Fact]
    public async Task GetAnalysis_ReturnsNotFoundForUnknownId()
    {
        var client = await SignedInClientAsync();

        var response = await client.GetAsync($"/analyses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListAnalyses_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/analyses");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListAnalyses_ReturnsTheAccountsAnalysesNewestFirst()
    {
        var client = await SignedInClientAsync();
        var first = await CreateAnalysisAsync(client);
        var second = await CreateAnalysisAsync(client);

        var list = await client.GetFromJsonAsync<List<AnalysisResponse>>("/analyses");

        Assert.NotNull(list);
        Assert.Equal(second.Id, list[0].Id);
        Assert.Equal(first.Id, list[1].Id);
        Assert.Equal("feedback.csv", list[0].SourceFilename);
    }

    [Fact]
    public async Task GetAnalysis_ReturnsThemesWithExactRepresentativeTexts()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(client);

        // Write results the way the worker does, straight into the schema:
        // two themes ordered by position, "Too slow" cited by the first one.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var verbatims = await db.Verbatims
                .Where(verbatim => verbatim.AnalysisId == analysis.Id)
                .OrderBy(verbatim => verbatim.Position)
                .ToListAsync();
            var performance = new Theme
            {
                AnalysisId = analysis.Id,
                Name = "Performance",
                Synthesis = "Speed disappoints.",
                Position = 0,
            };
            var praise = new Theme
            {
                AnalysisId = analysis.Id,
                Name = "Praise",
                Synthesis = "People are happy.",
                Position = 1,
            };
            db.Themes.AddRange(performance, praise);
            db.ThemeVerbatims.AddRange(
                new ThemeVerbatim { ThemeId = performance.Id, VerbatimId = verbatims[1].Id, Rank = 0 },
                new ThemeVerbatim { ThemeId = performance.Id, VerbatimId = verbatims[0].Id },
                new ThemeVerbatim { ThemeId = praise.Id, VerbatimId = verbatims[0].Id, Rank = 0 });
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<AnalysisDetailResponse>($"/analyses/{analysis.Id}");

        Assert.NotNull(detail);
        Assert.Equal(2, detail.Themes.Count);
        var first = detail.Themes[0];
        Assert.Equal("Performance", first.Name);
        Assert.Equal("Speed disappoints.", first.Synthesis);
        Assert.Equal(2, first.VerbatimCount);
        // The citation is the original row, word for word, with its source position.
        var cited = Assert.Single(first.Representatives);
        Assert.Equal("Too slow", cited.Text);
        Assert.Equal(1, cited.Position);
        Assert.Equal("Praise", detail.Themes[1].Name);
        // Every verbatim is attached to at least one theme: no loss to report.
        Assert.Equal(0, detail.UnclassifiedCount);
    }

    [Fact]
    public async Task GetAnalysis_OrdersRepresentativesByRank()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(
            client, "comment", "comment,score\nfirst,1\nsecond,2\nthird,3\n");

        // Attachments are inserted out of rank order on purpose: the reading
        // order must come from the ranks, not from insertion.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var verbatims = await db.Verbatims
                .Where(verbatim => verbatim.AnalysisId == analysis.Id)
                .OrderBy(verbatim => verbatim.Position)
                .ToListAsync();
            var theme = new Theme
            {
                AnalysisId = analysis.Id,
                Name = "Everything",
                Synthesis = "All of it.",
                Position = 0,
            };
            db.Themes.Add(theme);
            db.ThemeVerbatims.AddRange(
                new ThemeVerbatim { ThemeId = theme.Id, VerbatimId = verbatims[2].Id, Rank = 1 },
                new ThemeVerbatim { ThemeId = theme.Id, VerbatimId = verbatims[0].Id, Rank = 2 },
                new ThemeVerbatim { ThemeId = theme.Id, VerbatimId = verbatims[1].Id, Rank = 0 });
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<AnalysisDetailResponse>($"/analyses/{analysis.Id}");

        Assert.NotNull(detail);
        var cited = Assert.Single(detail.Themes).Representatives;
        Assert.Equal(["second", "third", "first"], cited.Select(r => r.Text));
    }

    [Fact]
    public async Task GetAnalysis_CountsAVerbatimInSeveralThemesOnce()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(
            client, "comment", "comment,score\nfirst,1\nsecond,2\nthird,3\n");

        // "first" supports two themes, "second" one, "third" none. Summing the
        // per-theme counts would say 3 classified out of 2 distinct — only a
        // count of verbatims without any attachment reports the loss right.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var verbatims = await db.Verbatims
                .Where(verbatim => verbatim.AnalysisId == analysis.Id)
                .OrderBy(verbatim => verbatim.Position)
                .ToListAsync();
            var onboarding = new Theme
            {
                AnalysisId = analysis.Id,
                Name = "Onboarding",
                Synthesis = "Getting started is easy.",
                Position = 0,
            };
            var pricing = new Theme
            {
                AnalysisId = analysis.Id,
                Name = "Pricing",
                Synthesis = "Plans feel fair.",
                Position = 1,
            };
            db.Themes.AddRange(onboarding, pricing);
            db.ThemeVerbatims.AddRange(
                new ThemeVerbatim { ThemeId = onboarding.Id, VerbatimId = verbatims[0].Id, Rank = 0 },
                new ThemeVerbatim { ThemeId = pricing.Id, VerbatimId = verbatims[0].Id, Rank = 0 },
                new ThemeVerbatim { ThemeId = onboarding.Id, VerbatimId = verbatims[1].Id });
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<AnalysisDetailResponse>($"/analyses/{analysis.Id}");

        Assert.NotNull(detail);
        Assert.Equal(1, detail.UnclassifiedCount);
    }

    [Fact]
    public async Task GetAnalysis_WithoutThemes_ReportsAllVerbatimsUnclassified()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(client);

        var detail = await client.GetFromJsonAsync<AnalysisDetailResponse>($"/analyses/{analysis.Id}");

        Assert.NotNull(detail);
        Assert.Equal(analysis.VerbatimCount, detail.UnclassifiedCount);
        Assert.Equal(2, detail.UnclassifiedCount);
    }

    [Fact]
    public async Task GetAnalysis_ExposesProgressAndError()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE analyses SET status = 'failed', processed_count = 1, error = 'Analysis stopped at its cost cap.' WHERE id = {analysis.Id}");
        }

        var detail = await client.GetFromJsonAsync<AnalysisDetailResponse>($"/analyses/{analysis.Id}");

        Assert.NotNull(detail);
        Assert.Equal("failed", detail.Status, ignoreCase: true);
        Assert.Equal(1, detail.ProcessedCount);
        Assert.Equal("Analysis stopped at its cost cap.", detail.Error);
        Assert.Empty(detail.Themes);
    }

    [Fact]
    public async Task ListAnalyses_DoesNotListAnotherAccountsAnalyses()
    {
        var owner = await SignedInClientAsync();
        var mine = await CreateAnalysisAsync(owner);

        var intruder = await SignedInClientAsync();
        var list = await intruder.GetFromJsonAsync<List<AnalysisResponse>>("/analyses");

        Assert.NotNull(list);
        Assert.DoesNotContain(list, analysis => analysis.Id == mine.Id);
    }

    [Fact]
    public async Task CreateAnalysis_OverTheRateLimit_Returns429()
    {
        // A dedicated factory isolates the tiny window from the other tests.
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:AnalysesPermitLimit", "2"));
        var client = limited.CreateClient();
        await SignInFlow.SignInAsync(
            client, factory.MailpitApiBaseAddress, $"{Guid.NewGuid():N}@example.test");

        // A fresh upload per analysis: creating one purges its source CSV, so
        // an upload is single-use.
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var uploadId = await UploadAsync(client, DefaultCsv);
            var created = await client.PostAsJsonAsync(
                "/analyses", new { uploadId, verbatimColumn = "comment" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var overLimit = await UploadAsync(client, DefaultCsv);
        var response = await client.PostAsJsonAsync(
            "/analyses", new { uploadId = overLimit, verbatimColumn = "comment" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task CreateAnalysis_PurgesTheSourceCsv()
    {
        var client = await SignedInClientAsync();
        var uploadId = await UploadAsync(client, DefaultCsv);

        var created = await client.PostAsJsonAsync(
            "/analyses", new { uploadId, verbatimColumn = "comment" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var content = await db.Uploads
            .Where(upload => upload.Id == uploadId)
            .Select(upload => upload.Content)
            .SingleAsync();
        Assert.Empty(content);
    }

    [Fact]
    public async Task DeleteAnalysis_RemovesItAndItsVerbatims()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(client);

        var response = await client.DeleteAsync($"/analyses/{analysis.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Analyses.AnyAsync(candidate => candidate.Id == analysis.Id));
        Assert.False(await db.Verbatims.AnyAsync(verbatim => verbatim.AnalysisId == analysis.Id));
    }

    [Fact]
    public async Task DeleteAnalysis_AlsoDeletesItsShareToken()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(client);
        await MarkSucceededAsync(analysis.Id);
        (await client.PostAsync($"/analyses/{analysis.Id}/share", null)).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync($"/analyses/{analysis.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.ShareTokens.AnyAsync(token => token.AnalysisId == analysis.Id));
    }

    [Fact]
    public async Task DeleteAnalysis_OfAnotherAccount_Returns404AndLeavesItIntact()
    {
        var owner = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(owner);
        var intruder = await SignedInClientAsync();

        var response = await intruder.DeleteAsync($"/analyses/{analysis.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Analyses.AnyAsync(candidate => candidate.Id == analysis.Id));
    }

    [Fact]
    public async Task DeleteAnalysis_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient().DeleteAsync($"/analyses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_ErasesTheAccountsAnalysesAndUploads()
    {
        var client = await SignedInClientAsync();
        var uploadId = await UploadAsync(client, DefaultCsv);
        var created = await client.PostAsJsonAsync(
            "/analyses", new { uploadId, verbatimColumn = "comment" });
        var analysis = await created.Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(analysis);

        var response = await client.DeleteAsync("/auth/account");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Analyses.AnyAsync(candidate => candidate.Id == analysis.Id));
        Assert.False(await db.Verbatims.AnyAsync(verbatim => verbatim.AnalysisId == analysis.Id));
        Assert.False(await db.Uploads.AnyAsync(upload => upload.Id == uploadId));
    }

    private async Task MarkSucceededAsync(Guid analysisId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE analyses SET status = 'succeeded', processed_count = verbatim_count WHERE id = {analysisId}");
    }
}