using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// The themes schema contract: constraints the database itself enforces,
/// whatever code writes to it — the worker writes these tables through raw
/// SQL (see docs/architecture.md, "le schéma est un contrat").
/// </summary>
public sealed class ThemesModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Themes_RequireAnExistingAnalysis()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Themes.Add(new Theme
        {
            AnalysisId = Guid.CreateVersion7(),
            Name = "orphan theme",
            Synthesis = "a synthesis without an analysis",
            Position = 0,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThemeVerbatims_RequireAnExistingVerbatim()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var theme = await SeedAnalysisWithThemeAsync(db);
        db.ThemeVerbatims.Add(new ThemeVerbatim
        {
            ThemeId = theme.Id,
            VerbatimId = Guid.CreateVersion7(),
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ThemeVerbatims_RejectAttachingTheSameVerbatimTwice()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var theme = await SeedAnalysisWithThemeAsync(db);
        var verbatim = new Verbatim { AnalysisId = theme.AnalysisId, Position = 0, Text = "too slow" };
        db.Verbatims.Add(verbatim);
        db.ThemeVerbatims.Add(new ThemeVerbatim { ThemeId = theme.Id, VerbatimId = verbatim.Id });
        await db.SaveChangesAsync();

        // Raw SQL on purpose: the worker writes this table without EF, the
        // duplicate guard must be the database's, not the change tracker's.
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO theme_verbatims (theme_id, verbatim_id, rank) VALUES ({theme.Id}, {verbatim.Id}, 0)"));
    }

    [Fact]
    public async Task Themes_AreDeletedWithTheirAnalysis_AttachmentsIncluded()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var theme = await SeedAnalysisWithThemeAsync(db);
        var verbatim = new Verbatim { AnalysisId = theme.AnalysisId, Position = 0, Text = "attached" };
        db.Verbatims.Add(verbatim);
        db.ThemeVerbatims.Add(new ThemeVerbatim { ThemeId = theme.Id, VerbatimId = verbatim.Id, Rank = 0 });
        await db.SaveChangesAsync();

        var analysis = await db.Analyses.SingleAsync(a => a.Id == theme.AnalysisId);
        db.Analyses.Remove(analysis);
        await db.SaveChangesAsync();

        Assert.False(await db.Themes.AnyAsync(t => t.AnalysisId == analysis.Id));
        Assert.False(await db.ThemeVerbatims.AnyAsync(tv => tv.ThemeId == theme.Id));
    }

    [Fact]
    public async Task ThemeVerbatims_RoundTripAttachedAndRepresentative()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var theme = await SeedAnalysisWithThemeAsync(db);
        var attached = new Verbatim { AnalysisId = theme.AnalysisId, Position = 0, Text = "supports the theme" };
        var cited = new Verbatim { AnalysisId = theme.AnalysisId, Position = 1, Text = "cited word for word" };
        db.Verbatims.AddRange(attached, cited);
        db.ThemeVerbatims.AddRange(
            new ThemeVerbatim { ThemeId = theme.Id, VerbatimId = attached.Id },
            new ThemeVerbatim { ThemeId = theme.Id, VerbatimId = cited.Id, Rank = 0 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await db.ThemeVerbatims
            .Where(tv => tv.ThemeId == theme.Id)
            .OrderBy(tv => tv.Rank == null)
            .ToListAsync();
        Assert.Equal(2, reloaded.Count);
        Assert.Equal(cited.Id, reloaded[0].VerbatimId);
        Assert.Equal(0, reloaded[0].Rank);
        Assert.Null(reloaded[1].Rank);
    }

    [Fact]
    public async Task Analyses_CarryPipelineDefaults()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = NewUser();
        db.Users.Add(user);
        var analysis = new Analysis { UserId = user.Id, CreatedAt = DateTimeOffset.UtcNow };
        db.Analyses.Add(analysis);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await db.Analyses.SingleAsync(a => a.Id == analysis.Id);
        Assert.Null(reloaded.HeartbeatAt);
        Assert.Null(reloaded.Error);
        Assert.Equal(0, reloaded.Attempts);
        Assert.Equal(0, reloaded.ProcessedCount);
        Assert.Equal(0, reloaded.InputTokens);
        Assert.Equal(0, reloaded.OutputTokens);
    }

    private static User NewUser() =>
        new() { Email = $"{Guid.NewGuid():N}@example.test", CreatedAt = DateTimeOffset.UtcNow };

    private static async Task<Theme> SeedAnalysisWithThemeAsync(AppDbContext db)
    {
        var user = NewUser();
        db.Users.Add(user);
        var analysis = new Analysis { UserId = user.Id, CreatedAt = DateTimeOffset.UtcNow };
        db.Analyses.Add(analysis);
        var theme = new Theme
        {
            AnalysisId = analysis.Id,
            Name = "Performance",
            Synthesis = "Users report the app feels slow.",
            Position = 0,
        };
        db.Themes.Add(theme);
        await db.SaveChangesAsync();
        return theme;
    }
}