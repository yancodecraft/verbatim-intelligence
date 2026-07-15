using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// The magic-link contract, exercised end to end: requesting a link sends a
/// real e-mail (read back through Mailpit's API), verifying it establishes a
/// session cookie, and every rejection path stays a 401.
/// </summary>
public sealed partial class AuthEndpointsTests(ApiFactory factory)
    : IClassFixture<ApiFactory>, IAsyncLifetime, IDisposable
{
    private readonly HttpClient _mailpit = new() { BaseAddress = factory.MailpitApiBaseAddress };

    [GeneratedRegex("http://localhost:5180/verify\\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex VerifyLink();

    public async Task InitializeAsync() =>
        // Each test starts with an empty inbox.
        await _mailpit.DeleteAsync("api/v1/messages");

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _mailpit.Dispose();

    [Fact]
    public async Task RequestMagicLink_SendsAVerifyLinkByEmail()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/magic-link", new { email = "Alice@Example.test " });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadSingleMessageBodyAsync("alice@example.test");
        Assert.Matches(VerifyLink(), body);
    }

    [Fact]
    public async Task RequestMagicLink_AnswersTheSameForNewAndKnownAccounts()
    {
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync(
            "/auth/magic-link", new { email = "repeat@example.test" });
        var second = await client.PostAsJsonAsync(
            "/auth/magic-link", new { email = "repeat@example.test" });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
    }

    [Fact]
    public async Task Verify_WithTheMailedToken_EstablishesASession()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/auth/magic-link", new { email = "bob@example.test" });
        var token = await ReadMailedTokenAsync("bob@example.test");

        var verify = await client.PostAsJsonAsync("/auth/verify", new { token });

        Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
        var cookie = Assert.Single(verify.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("vi_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<JsonElement>("/auth/me");
        Assert.Equal("bob@example.test", me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Verify_ConsumesTheTokenOnFirstUse()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/auth/magic-link", new { email = "once@example.test" });
        var token = await ReadMailedTokenAsync("once@example.test");

        var first = await client.PostAsJsonAsync("/auth/verify", new { token });
        var second = await factory.CreateClient().PostAsJsonAsync("/auth/verify", new { token });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
    }

    [Fact]
    public async Task Verify_RejectsAnExpiredToken()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Email = "late@example.test", CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        var raw = Tokens.CreateRaw();
        db.LoginTokens.Add(new LoginToken
        {
            UserId = user.Id,
            TokenHash = Tokens.Hash(raw),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-15),
        });
        await db.SaveChangesAsync();

        var verify = await factory.CreateClient()
            .PostAsJsonAsync("/auth/verify", new { token = raw });

        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    [Fact]
    public async Task Verify_RejectsAnUnknownToken()
    {
        var verify = await factory.CreateClient()
            .PostAsJsonAsync("/auth/verify", new { token = Tokens.CreateRaw() });

        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_WithoutAnEmail_Returns400()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/auth/magic-link", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Verify_WithoutAToken_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/auth/verify", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_IsRateLimitedPerAddress()
    {
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var accepted = await client.PostAsJsonAsync(
                "/auth/magic-link", new { email = "flooded@example.test" });
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var throttled = await client.PostAsJsonAsync(
            "/auth/magic-link", new { email = "flooded@example.test" });

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutASession_Returns401()
    {
        var me = await factory.CreateClient().GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesTheSessionServerSide()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/auth/magic-link", new { email = "gone@example.test" });
        var token = await ReadMailedTokenAsync("gone@example.test");
        await client.PostAsJsonAsync("/auth/verify", new { token });

        var logout = await client.PostAsync("/auth/logout", content: null);
        var me = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WithoutASession_Returns401()
    {
        var response = await factory.CreateClient().DeleteAsync("/auth/account");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_ClearsTheSessionAndRemovesTheUser()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var client = factory.CreateClient();
        await SignInFlow.SignInAsync(client, factory.MailpitApiBaseAddress, email);

        var response = await client.DeleteAsync("/auth/account");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        // The session no longer resolves: the same client is anonymous again.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(user => user.Email == email));
    }

    [Fact]
    public async Task RequestMagicLink_WithAMalformedEmail_Returns400NotServerError()
    {
        // Contains '@' (the old check passed) but the mail library cannot parse
        // it — this used to reach the sender and throw a 500.
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/auth/magic-link", new { email = "broken@" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_IsRateLimitedPerClient()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:MagicLinkPermitLimit", "2"));
        var client = limited.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(
            "/auth/magic-link", new { email = "a@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(
            "/auth/magic-link", new { email = "b@example.test" })).StatusCode);

        var response = await client.PostAsJsonAsync(
            "/auth/magic-link", new { email = "c@example.test" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Verify_IsRateLimitedPerClient()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("RateLimiting:VerifyPermitLimit", "2"));
        var client = limited.CreateClient();

        await client.PostAsJsonAsync("/auth/verify", new { token = Tokens.CreateRaw() });
        await client.PostAsJsonAsync("/auth/verify", new { token = Tokens.CreateRaw() });

        var response = await client.PostAsJsonAsync(
            "/auth/verify", new { token = Tokens.CreateRaw() });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Verify_UnderConcurrentReplay_EstablishesExactlyOneSession()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/auth/magic-link", new { email = "race@example.test" });
        var token = await ReadMailedTokenAsync("race@example.test");

        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            factory.CreateClient().PostAsJsonAsync("/auth/verify", new { token })));

        Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(3, attempts.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    private async Task<string> ReadMailedTokenAsync(string recipient)
    {
        var body = await ReadSingleMessageBodyAsync(recipient);
        return VerifyLink().Match(body).Groups[1].Value;
    }

    private async Task<string> ReadSingleMessageBodyAsync(string recipient)
    {
        var inbox = await _mailpit.GetFromJsonAsync<JsonElement>("api/v1/messages");
        var message = Assert.Single(
            inbox.GetProperty("messages").EnumerateArray(),
            m => m.GetProperty("To")[0].GetProperty("Address").GetString() == recipient);
        var detail = await _mailpit.GetFromJsonAsync<JsonElement>(
            $"api/v1/message/{message.GetProperty("ID").GetString()}");
        return detail.GetProperty("Text").GetString()!;
    }
}