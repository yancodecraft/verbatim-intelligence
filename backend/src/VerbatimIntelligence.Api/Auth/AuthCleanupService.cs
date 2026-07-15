using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Auth;

/// <summary>
/// Runs <see cref="AuthCleanup"/> on a timer. Each pass takes a fresh scope so
/// the DbContext is short-lived; a transient failure is swallowed and retried
/// on the next tick rather than tearing the host down.
/// </summary>
public sealed class AuthCleanupService(
    IServiceScopeFactory scopeFactory, TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await AuthCleanup.RunAsync(db, clock.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
#pragma warning disable CA1031 // A background sweep must survive transient DB errors.
            catch (Exception)
            {
                // Swallowed on purpose: retry next tick.
            }
#pragma warning restore CA1031
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}