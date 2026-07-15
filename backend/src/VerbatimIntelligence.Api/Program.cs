using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

using StackExchange.Redis;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;
using VerbatimIntelligence.Api.Email;
using VerbatimIntelligence.Api.Uploads;

var builder = WebApplication.CreateBuilder(args);

// Room for a 5 MB upload (the CSV contract) plus multipart overhead; the
// endpoint enforces the 5 MB rule itself with a clear message.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 6 * 1024 * 1024);

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Database")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Database"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Redis")));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
// ValidateOnStart materializes the options at boot: missing Email settings
// fail fast (required members), not on the first e-mail sent.
builder.Services.AddOptions<EmailOptions>()
    .BindConfiguration(EmailOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddOptions<AuthOptions>()
    .BindConfiguration(AuthOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<CurrentAccountAccessor>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

// Periodic auth-table hygiene (docs/security-review.md, D5). Disabled in tests,
// which drive the cleanup routine directly against a throwaway database.
if (builder.Configuration.GetValue("Auth:CleanupEnabled", true))
{
    builder.Services.AddHostedService<AuthCleanupService>();
}

// Behind the compose reverse proxy the backend only sees the proxy's address.
// Trust the private Docker network so X-Forwarded-For yields the real client
// IP, which per-client rate limiting keys on. The backend has no published
// port, so a public client cannot reach Kestrel to spoof the header, and
// ForwardLimit 1 takes only the hop the proxy itself appended.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    foreach (var network in builder.Configuration
        .GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

// Anti-abuse windows, each scoped per client (RateLimiting.ClientPartitionKey)
// so one caller can never saturate them for everyone: the public shared-report
// endpoint, plus the authenticated upload and analysis-creation writes whose
// cost (storage, LLM tokens) makes them worth bounding (docs/security-review.md).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    AddClientWindow(options, ShareEndpoints.RateLimitPolicy,
        builder.Configuration.GetValue<int?>("RateLimiting:SharedPermitLimit") ?? 60);
    AddClientWindow(options, UploadsEndpoints.RateLimitPolicy,
        builder.Configuration.GetValue<int?>("RateLimiting:UploadsPermitLimit") ?? 20);
    AddClientWindow(options, AnalysesEndpoints.RateLimitPolicy,
        builder.Configuration.GetValue<int?>("RateLimiting:AnalysesPermitLimit") ?? 10);
    AddClientWindow(options, AuthEndpoints.MagicLinkRateLimitPolicy,
        builder.Configuration.GetValue<int?>("RateLimiting:MagicLinkPermitLimit") ?? 10);
    AddClientWindow(options, AuthEndpoints.VerifyRateLimitPolicy,
        builder.Configuration.GetValue<int?>("RateLimiting:VerifyPermitLimit") ?? 20);
});

static void AddClientWindow(RateLimiterOptions options, string policy, int permitLimit) =>
    options.AddPolicy(policy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            VerbatimIntelligence.Api.RateLimiting.ClientPartitionKey(http),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Dev and tests only; production applies migrations as a deploy step.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// No UseHttpsRedirection: TLS terminates at the reverse proxy; the app only
// ever serves plain HTTP inside the compose network. ForwardedHeaders first,
// so the client IP is resolved before the rate limiter partitions on it.
app.UseForwardedHeaders();
app.UseRateLimiter();
app.MapHealthChecks("/health");
app.MapAuth();
app.MapUploads();
app.MapAnalyses();
app.MapShares();

await app.RunAsync();

// Exposes the implicit entry-point class to WebApplicationFactory in tests.
public partial class Program
{
    protected Program() { }
}