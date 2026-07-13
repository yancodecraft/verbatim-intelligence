using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// The auth schema contract: constraints the database itself must enforce,
/// whatever code writes to it.
/// </summary>
public sealed class AuthModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Users_RejectDuplicateEmails()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Users.Add(new User { Email = "dupe@example.test", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.Users.Add(new User { Email = "dupe@example.test", CreatedAt = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LoginTokens_RequireAnExistingUser()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.LoginTokens.Add(new LoginToken
        {
            UserId = Guid.CreateVersion7(),
            TokenHash = new string('a', 64),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sessions_AreDeletedWithTheirUser()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User { Email = "cascade@example.test", CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        db.Sessions.Add(new Session
        {
            UserId = user.Id,
            TokenHash = new string('b', 64),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        Assert.False(await db.Sessions.AnyAsync(s => s.UserId == user.Id));
    }
}