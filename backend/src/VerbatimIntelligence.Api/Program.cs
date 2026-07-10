var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// No UseHttpsRedirection: TLS terminates at the reverse proxy; the app only
// ever serves plain HTTP inside the compose network.
app.MapHealthChecks("/health");

await app.RunAsync();

// Exposes the implicit entry-point class to WebApplicationFactory in tests.
public partial class Program
{
    protected Program() { }
}