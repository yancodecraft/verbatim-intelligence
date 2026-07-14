using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// Uploading a CSV under the contract: account-scoped, a clean 201 with a
/// preview on success, a plain-text 400 message on every rejection.
/// </summary>
public sealed class UploadsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record UploadResponse(
        Guid Id, string Filename, List<string> Columns, List<List<string>> SampleRows, int RowCount);

    private sealed record UploadError(string Message);

    private static MultipartFormDataContent CsvContent(byte[] bytes, string filename)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", filename } };
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateClient();
        await SignInFlow.SignInAsync(
            client, factory.MailpitApiBaseAddress, $"{Guid.NewGuid():N}@example.test");
        return client;
    }

    [Fact]
    public async Task Upload_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient().PostAsync(
            "/uploads", CsvContent(Encoding.UTF8.GetBytes("verbatim\nfoo\n"), "feedback.csv"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithValidCsv_Returns201WithPreview()
    {
        var client = await SignedInClientAsync();
        var csv = "comment,score\nr0,0\nr1,1\nr2,2\nr3,3\nr4,4\nr5,5\n";

        var response = await client.PostAsync(
            "/uploads", CsvContent(Encoding.UTF8.GetBytes(csv), "feedback.csv"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("feedback.csv", body.Filename);
        Assert.Equal(["comment", "score"], body.Columns);
        Assert.Equal(6, body.RowCount);
        // The preview is capped at five rows.
        Assert.Equal(5, body.SampleRows.Count);
        Assert.Equal(["r0", "0"], body.SampleRows[0]);
    }

    [Fact]
    public async Task Upload_PersistsTheFileForTheAccount()
    {
        var client = await SignedInClientAsync();
        var response = await client.PostAsync(
            "/uploads", CsvContent(Encoding.UTF8.GetBytes("verbatim\nhi\n"), "one.csv"));
        var body = await response.Content.ReadFromJsonAsync<UploadResponse>();
        Assert.NotNull(body);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Uploads.SingleAsync(upload => upload.Id == body.Id);
        Assert.Equal("one.csv", stored.Filename);
        Assert.Equal(1, stored.RowCount);
    }

    [Fact]
    public async Task Upload_WithEmptyFile_Returns400WithMessage()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/uploads", CsvContent([], "empty.csv"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<UploadError>();
        Assert.NotNull(error);
        Assert.Contains("empty", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_WithoutAFilePart_Returns400()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/uploads", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OverTheRateLimit_Returns429()
    {
        // A dedicated factory isolates the tiny window from the other tests.
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:UploadsPermitLimit", "2"));
        var client = limited.CreateClient();
        await SignInFlow.SignInAsync(
            client, factory.MailpitApiBaseAddress, $"{Guid.NewGuid():N}@example.test");
        var csv = Encoding.UTF8.GetBytes("verbatim\nfoo\n");

        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsync("/uploads", CsvContent(csv, "a.csv"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsync("/uploads", CsvContent(csv, "b.csv"))).StatusCode);

        var response = await client.PostAsync("/uploads", CsvContent(csv, "c.csv"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}