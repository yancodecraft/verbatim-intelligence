using System.Net;
using System.Net.Http.Json;

namespace VerbatimIntelligence.Api.Tests;

public class AnalysesEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record AnalysisResponse(Guid Id, string Status, DateTimeOffset CreatedAt);

    [Fact]
    public async Task PostAnalyses_CreatesPendingAnalysis()
    {
        var client = factory.CreateClient();

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
        var client = factory.CreateClient();
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
    public async Task GetAnalyses_ReturnsNotFoundForUnknownId()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/analyses/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}