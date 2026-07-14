using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// The share tokens schema contract: a share is a row pointing at its
/// analysis, holding only the hash of the raw token. The database enforces
/// one active link per analysis and deletes shares with their analysis
/// (docs/v1-spec.md, "corpus → analyse → partages").
/// </summary>
public sealed class ShareTokenModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ShareTokens_RequireAnExistingAnalysis()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ShareTokens.Add(NewShareToken(Guid.CreateVersion7()));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ShareTokens_AreDeletedWithTheirAnalysis()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var analysis = await SeedAnalysisAsync(db);
        db.ShareTokens.Add(NewShareToken(analysis.Id));
        await db.SaveChangesAsync();

        db.Analyses.Remove(analysis);
        await db.SaveChangesAsync();

        Assert.False(await db.ShareTokens.AnyAsync(token => token.AnalysisId == analysis.Id));
    }

    [Fact]
    public async Task ShareTokens_RejectADuplicateTokenHash()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var first = await SeedAnalysisAsync(db);
        var second = await SeedAnalysisAsync(db);
        var hash = Tokens.Hash(Tokens.CreateRaw());
        db.ShareTokens.Add(new ShareToken
        {
            AnalysisId = first.Id,
            TokenHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        db.ShareTokens.Add(new ShareToken
        {
            AnalysisId = second.Id,
            TokenHash = hash,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ShareTokens_RejectASecondTokenForTheSameAnalysis()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var analysis = await SeedAnalysisAsync(db);
        db.ShareTokens.Add(NewShareToken(analysis.Id));
        await db.SaveChangesAsync();

        // One active link per analysis, guaranteed by the schema — two
        // concurrent creations cannot leave two live links behind.
        db.ShareTokens.Add(NewShareToken(analysis.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static ShareToken NewShareToken(Guid analysisId) =>
        new()
        {
            AnalysisId = analysisId,
            TokenHash = Tokens.Hash(Tokens.CreateRaw()),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static async Task<Analysis> SeedAnalysisAsync(AppDbContext db)
    {
        var user = new User
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        var analysis = new Analysis { UserId = user.Id, CreatedAt = DateTimeOffset.UtcNow };
        db.Analyses.Add(analysis);
        await db.SaveChangesAsync();
        return analysis;
    }
}