using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

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

// Anti-abuse on the public shared-report endpoint (practices.md). A single
// global window, not per-IP: behind the reverse proxy the app only sees the
// proxy's address, so an IP partition would be global in disguise anyway.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ShareEndpoints.RateLimitPolicy, _ =>
        RateLimitPartition.GetFixedWindowLimiter("shared-report", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration
                    .GetValue<int?>("RateLimiting:SharedPermitLimit") ?? 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

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
// ever serves plain HTTP inside the compose network.
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