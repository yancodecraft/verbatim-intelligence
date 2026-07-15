using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// The periodic auth-table sweep: it drops expired tokens and sessions and
/// unused, long-abandoned accounts — but never an account that still holds
/// data, a live session, or a sign-in in progress. Its own factory (own
/// database) keeps the whole-table sweep isolated from the other suites.
/// </summary>
public sealed class AuthCleanupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Session SessionFor(Guid userId, DateTimeOffset createdAt, DateTimeOffset expiresAt) =>
        new()
        {
            UserId = userId,
            TokenHash = Tokens.Hash(Tokens.CreateRaw()),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
        };

    [Fact]
    public async Task RunAsync_DropsExpiredAndUnusedButKeepsEverythingLiveOrOwned()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var old = now.AddDays(-40);

        // Never used, old: no session, token or data -> removed.
        var abandoned = new User { Email = $"{Guid.NewGuid():N}@example.test", CreatedAt = old };
        // Old but with a live session -> kept.
        var active = new User { Email = $"{Guid.NewGuid():N}@example.test", CreatedAt = old };
        // Old, no session, but owns an analysis -> kept (holds data).
        var owner = new User { Email = $"{Guid.NewGuid():N}@example.test", CreatedAt = old };
        // Within the grace window -> kept even though it is empty.
        var recent = new User { Email = $"{Guid.NewGuid():N}@example.test", CreatedAt = now.AddDays(-1) };
        db.Users.AddRange(abandoned, active, owner, recent);

        db.Sessions.Add(SessionFor(active.Id, old, now.AddDays(20)));
        db.Sessions.Add(SessionFor(recent.Id, old, now.AddDays(-2)));
        db.LoginTokens.Add(new LoginToken
        {
            UserId = abandoned.Id,
            TokenHash = Tokens.Hash(Tokens.CreateRaw()),
            CreatedAt = old,
            ExpiresAt = old,
        });
        db.Analyses.Add(new Analysis
        {
            UserId = owner.Id,
            CreatedAt = old,
            SourceFilename = "feedback.csv",
        });
        await db.SaveChangesAsync();

        await AuthCleanup.RunAsync(db, now);

        // Expired session and token are gone.
        Assert.False(await db.Sessions.AnyAsync(s => s.UserId == recent.Id));
        Assert.False(await db.LoginTokens.AnyAsync(t => t.UserId == abandoned.Id));
        // The live session survives.
        Assert.True(await db.Sessions.AnyAsync(s => s.UserId == active.Id));
        // Only the abandoned account is removed.
        Assert.False(await db.Users.AnyAsync(u => u.Id == abandoned.Id));
        Assert.True(await db.Users.AnyAsync(u => u.Id == active.Id));
        Assert.True(await db.Users.AnyAsync(u => u.Id == owner.Id));
        Assert.True(await db.Users.AnyAsync(u => u.Id == recent.Id));
    }
}