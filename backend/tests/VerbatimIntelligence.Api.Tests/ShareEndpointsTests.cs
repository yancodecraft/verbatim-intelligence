using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// Sharing an analysis: the owner creates or revokes a single public link
/// (scoped endpoints), anyone holding the link reads the report (public
/// endpoint, no session). The token itself is the access capability — it
/// designates its analysis, so no cross-analysis confusion is possible.
/// </summary>
public class ShareEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string DefaultCsv = "comment,score\nGreat product,9\nToo slow,3\n";

    private sealed record AnalysisResponse(Guid Id, string Status, int VerbatimCount);

    private sealed record ShareResponse(string Url);

    private sealed record UploadResult(Guid Id);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        await SignInFlow.SignInAsync(
            client, factory.MailpitApiBaseAddress, $"{Guid.NewGuid():N}@example.test");
        return client;
    }

    private async Task<AnalysisResponse> CreateSucceededAnalysisAsync(HttpClient client)
    {
        var analysis = await CreateAnalysisAsync(client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE analyses SET status = 'succeeded', processed_count = verbatim_count WHERE id = {analysis.Id}");
        return analysis;
    }

    private static async Task<AnalysisResponse> CreateAnalysisAsync(HttpClient client)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(DefaultCsv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var upload = await client.PostAsync(
            "/uploads", new MultipartFormDataContent { { file, "file", "feedback.csv" } });
        upload.EnsureSuccessStatusCode();
        var uploadResult = await upload.Content.ReadFromJsonAsync<UploadResult>();
        Assert.NotNull(uploadResult);

        var response = await client.PostAsJsonAsync(
            "/analyses", new { uploadId = uploadResult.Id, verbatimColumn = "comment" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var analysis = await response.Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(analysis);
        return analysis;
    }

    private static async Task<string> ShareAsync(HttpClient client, Guid analysisId)
    {
        var response = await client.PostAsync($"/analyses/{analysisId}/share", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShareResponse>();
        Assert.NotNull(body);
        return body.Url;
    }

    private static string TokenFrom(string url)
    {
        var match = Regex.Match(url, "/shared/([A-Za-z0-9_-]+)$");
        Assert.True(match.Success, $"unexpected share url: {url}");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task PostShare_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient()
            .PostAsync($"/analyses/{Guid.NewGuid()}/share", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteShare_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient()
            .DeleteAsync($"/analyses/{Guid.NewGuid()}/share");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostShare_OnASucceededAnalysis_ReturnsTheShareUrl()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);

        var url = await ShareAsync(client, analysis.Id);

        // 32 CSPRNG bytes, base64url: 43 chars, URL-safe alphabet.
        Assert.Matches("^http://localhost:5180/shared/[A-Za-z0-9_-]{43}$", url);
    }

    [Fact]
    public async Task PostShare_StoresOnlyTheTokenHash()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);

        var raw = TokenFrom(await ShareAsync(client, analysis.Id));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ShareTokens.SingleAsync(
            token => token.AnalysisId == analysis.Id);
        Assert.Equal(Tokens.Hash(raw), stored.TokenHash);
        Assert.NotEqual(raw, stored.TokenHash);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("failed")]
    public async Task PostShare_OnAnUnfinishedAnalysis_Returns400(string status)
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateAnalysisAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE analyses SET status = {status} WHERE id = {analysis.Id}");
        }

        var response = await client.PostAsync($"/analyses/{analysis.Id}/share", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostShare_OnAnotherAccountsAnalysis_Returns404()
    {
        var owner = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(owner);

        var intruder = await SignedInClientAsync();
        var response = await intruder.PostAsync($"/analyses/{analysis.Id}/share", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostShare_WithUnknownAnalysis_Returns404()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync($"/analyses/{Guid.NewGuid()}/share", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostShare_Twice_ReplacesTheToken()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);

        var oldToken = TokenFrom(await ShareAsync(client, analysis.Id));
        var newToken = TokenFrom(await ShareAsync(client, analysis.Id));

        var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/shared/{oldToken}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await anonymous.GetAsync($"/shared/{newToken}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ShareTokens.CountAsync(
            token => token.AnalysisId == analysis.Id));
    }

    [Fact]
    public async Task DeleteShare_RevokesTheLink()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);
        var token = TokenFrom(await ShareAsync(client, analysis.Id));

        var response = await client.DeleteAsync($"/analyses/{analysis.Id}/share");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var anonymous = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/shared/{token}")).StatusCode);
    }

    [Fact]
    public async Task DeleteShare_WhenNotShared_Returns204()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);

        var response = await client.DeleteAsync($"/analyses/{analysis.Id}/share");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteShare_OnAnotherAccountsAnalysis_Returns404()
    {
        var owner = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(owner);
        await ShareAsync(owner, analysis.Id);

        var intruder = await SignedInClientAsync();
        var response = await intruder.DeleteAsync($"/analyses/{analysis.Id}/share");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalysis_ExposesWhetherItIsShared()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);

        Assert.False(await SharedFlagAsync(client, analysis.Id));

        await ShareAsync(client, analysis.Id);
        Assert.True(await SharedFlagAsync(client, analysis.Id));

        await client.DeleteAsync($"/analyses/{analysis.Id}/share");
        Assert.False(await SharedFlagAsync(client, analysis.Id));
    }

    [Fact]
    public async Task GetShared_WithoutASession_ReturnsTheReport()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);
        await SeedThemeAsync(analysis.Id);
        var token = TokenFrom(await ShareAsync(client, analysis.Id));

        var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/shared/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = report.RootElement;
        Assert.Equal("feedback.csv", root.GetProperty("sourceFilename").GetString());
        Assert.Equal(2, root.GetProperty("verbatimCount").GetInt32());
        Assert.Equal(1, root.GetProperty("unclassifiedCount").GetInt32());
        var theme = Assert.Single(root.GetProperty("themes").EnumerateArray().ToList());
        Assert.Equal("Performance", theme.GetProperty("name").GetString());
        // The citation is the original row, word for word, with its position.
        var cited = Assert.Single(theme.GetProperty("representatives").EnumerateArray().ToList());
        Assert.Equal("Too slow", cited.GetProperty("text").GetString());
        Assert.Equal(1, cited.GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task GetShared_ExposesOnlyPublicFields()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);
        var token = TokenFrom(await ShareAsync(client, analysis.Id));

        var response = await factory.CreateClient().GetAsync($"/shared/{token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // No internal id, no pipeline telemetry: the report is content only.
        foreach (var hidden in new[] { "id", "status", "processedCount", "error" })
        {
            Assert.False(
                report.RootElement.TryGetProperty(hidden, out _),
                $"public report must not expose '{hidden}'");
        }
    }

    [Fact]
    public async Task GetShared_WithUnknownToken_Returns404()
    {
        var response = await factory.CreateClient()
            .GetAsync($"/shared/{Tokens.CreateRaw()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetShared_SetsNoStoreCacheControl()
    {
        var client = await SignedInClientAsync();
        var analysis = await CreateSucceededAnalysisAsync(client);
        var token = TokenFrom(await ShareAsync(client, analysis.Id));

        var response = await factory.CreateClient().GetAsync($"/shared/{token}");

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task GetShared_OverTheRateLimit_Returns429()
    {
        // A dedicated factory isolates the tiny window from the other tests.
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:SharedPermitLimit", "2"));
        var anonymous = limited.CreateClient();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/shared/{Tokens.CreateRaw()}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/shared/{Tokens.CreateRaw()}")).StatusCode);

        var response = await anonymous.GetAsync($"/shared/{Tokens.CreateRaw()}");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private static async Task<bool> SharedFlagAsync(HttpClient client, Guid analysisId)
    {
        using var detail = JsonDocument.Parse(
            await client.GetStringAsync($"/analyses/{analysisId}"));
        return detail.RootElement.GetProperty("shared").GetBoolean();
    }

    private async Task SeedThemeAsync(Guid analysisId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verbatims = await db.Verbatims
            .Where(verbatim => verbatim.AnalysisId == analysisId)
            .OrderBy(verbatim => verbatim.Position)
            .ToListAsync();
        var theme = new Theme
        {
            AnalysisId = analysisId,
            Name = "Performance",
            Synthesis = "Speed disappoints.",
            Position = 0,
        };
        db.Themes.Add(theme);
        db.ThemeVerbatims.Add(new ThemeVerbatim
        {
            ThemeId = theme.Id,
            VerbatimId = verbatims[1].Id,
            Rank = 0,
        });
        await db.SaveChangesAsync();
    }
}