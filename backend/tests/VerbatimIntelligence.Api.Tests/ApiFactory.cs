using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// Boots the API against throwaway Postgres, Redis and Mailpit containers,
/// so integration tests exercise the real engines, never fakes.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "postgres:18-alpine@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder(
        "redis:8-alpine@sha256:9d317178eceac8454a2284a9e6df2466b93c745529947f0cd42a0fa9609d7005")
        .Build();

    private readonly IContainer _mailpit = new ContainerBuilder(
        "axllent/mailpit@sha256:5a49a77c5bdbe7c5474450b4f46348d09949df3695257729c93a30369382d4f6")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
            request => request.ForPort(8025).ForPath("/readyz")))
        .Build();

    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>Base address of Mailpit's REST API, to read delivered mail.</summary>
    public Uri MailpitApiBaseAddress =>
        new($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}");

    public async Task InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _mailpit.StartAsync());

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await _mailpit.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Database", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Email:SmtpHost", _mailpit.Hostname);
        builder.UseSetting("Email:SmtpPort",
            _mailpit.GetMappedPublicPort(1025).ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Email:From", "noreply@verbatim.test");
        builder.UseSetting("Auth:PublicBaseUrl", "http://localhost:5180");
        builder.UseSetting("Auth:SecureCookies", "false");
        // The background sweep is exercised directly (AuthCleanupTests), not
        // through the host, so it never races the other tests' data.
        builder.UseSetting("Auth:CleanupEnabled", "false");
        // Headroom so the suite's own traffic never trips a limit; the tests
        // that assert throttling use a dedicated factory with a tiny window.
        builder.UseSetting("RateLimiting:SharedPermitLimit", "1000");
        builder.UseSetting("RateLimiting:UploadsPermitLimit", "1000");
        builder.UseSetting("RateLimiting:AnalysesPermitLimit", "1000");
        builder.UseSetting("RateLimiting:MagicLinkPermitLimit", "1000");
        builder.UseSetting("RateLimiting:VerifyPermitLimit", "1000");
    }
}