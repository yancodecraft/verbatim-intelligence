using System.Net;
using System.Net.Http.Json;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// Analyses are account-scoped: no session means 401, and another account's
/// analysis is a 404 — indistinguishable from a non-existent one.
/// </summary>
public class AnalysesEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record AnalysisResponse(
        Guid Id, string Status, DateTimeOffset CreatedAt, string SourceFilename, int VerbatimCount);

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        await SignInFlow.SignInAsync(
            client, factory.MailpitApiBaseAddress, $"{Guid.NewGuid():N}@example.test");
        return client;
    }

    [Fact]
    public async Task PostAnalyses_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient().PostAsync("/analyses", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAnalysis_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient().GetAsync($"/analyses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyses_CreatesPendingAnalysis()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/analyses", content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var analysis = await response.Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(analysis);
        Assert.NotEqual(Guid.Empty, analysis.Id);
        Assert.Equal("pending", analysis.Status);
        Assert.Equal($"/analyses/{analysis.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task GetAnalyses_ReturnsCreatedAnalysis()
    {
        var client = await SignedInClientAsync();
        var created = await (await client.PostAsync("/analyses", content: null))
            .Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(created);

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
        var created = await (await owner.PostAsync("/analyses", content: null))
            .Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(created);

        var intruder = await SignedInClientAsync();
        var response = await intruder.GetAsync($"/analyses/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostAnalyses_EnqueuesAnalysisId()
    {
        var client = await SignedInClientAsync();

        var created = await (await client.PostAsync("/analyses", content: null))
            .Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(created);

        await using var redis = await StackExchange.Redis.ConnectionMultiplexer
            .ConnectAsync(factory.RedisConnectionString);
        var queued = await redis.GetDatabase()
            .ListRangeAsync(Analyses.RedisKeys.PendingAnalyses);
        Assert.Contains(created.Id.ToString(), queued.Select(value => (string?)value));
    }

    [Fact]
    public async Task GetAnalyses_ReturnsNotFoundForUnknownId()
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
        var first = await (await client.PostAsync("/analyses", content: null))
            .Content.ReadFromJsonAsync<AnalysisResponse>();
        var second = await (await client.PostAsync("/analyses", content: null))
            .Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(first);
        Assert.NotNull(second);

        var list = await client.GetFromJsonAsync<List<AnalysisResponse>>("/analyses");

        Assert.NotNull(list);
        Assert.Equal(second.Id, list[0].Id);
        Assert.Equal(first.Id, list[1].Id);
        Assert.Equal("pending", list[0].Status);
    }

    [Fact]
    public async Task ListAnalyses_DoesNotListAnotherAccountsAnalyses()
    {
        var owner = await SignedInClientAsync();
        var mine = await (await owner.PostAsync("/analyses", content: null))
            .Content.ReadFromJsonAsync<AnalysisResponse>();
        Assert.NotNull(mine);

        var intruder = await SignedInClientAsync();
        var list = await intruder.GetFromJsonAsync<List<AnalysisResponse>>("/analyses");

        Assert.NotNull(list);
        Assert.DoesNotContain(list, analysis => analysis.Id == mine.Id);
    }
}