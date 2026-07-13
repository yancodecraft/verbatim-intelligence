using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;
using VerbatimIntelligence.Api.Uploads;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// The ingestion schema contract: constraints the database itself enforces,
/// whatever code writes to it (see docs/architecture.md, "le schéma est un
/// contrat").
/// </summary>
public sealed class IngestionModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Verbatims_RequireAnExistingAnalysis()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Verbatims.Add(new Verbatim
        {
            AnalysisId = Guid.CreateVersion7(),
            Position = 0,
            Text = "orphan",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Verbatims_AreDeletedWithTheirAnalysis()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User { Email = $"{Guid.NewGuid():N}@example.test", CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        var analysis = new Analysis { UserId = user.Id, CreatedAt = DateTimeOffset.UtcNow };
        db.Analyses.Add(analysis);
        db.Verbatims.Add(new Verbatim { AnalysisId = analysis.Id, Position = 0, Text = "kept in the corpus" });
        await db.SaveChangesAsync();

        db.Analyses.Remove(analysis);
        await db.SaveChangesAsync();

        Assert.False(await db.Verbatims.AnyAsync(v => v.AnalysisId == analysis.Id));
    }

    [Fact]
    public async Task Uploads_AreDeletedWithTheirUser()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User { Email = $"{Guid.NewGuid():N}@example.test", CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        db.Uploads.Add(new Upload
        {
            UserId = user.Id,
            Filename = "feedback.csv",
            Content = [1, 2, 3],
            Columns = ["verbatim", "score"],
            RowCount = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        Assert.False(await db.Uploads.AnyAsync(u => u.UserId == user.Id));
    }

    [Fact]
    public async Task Uploads_RoundTripColumnsAsJson()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User { Email = $"{Guid.NewGuid():N}@example.test", CreatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        var upload = new Upload
        {
            UserId = user.Id,
            Filename = "feedback.csv",
            Content = [42],
            Columns = ["comment", "nps", "segment"],
            RowCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Uploads.Add(upload);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var reloaded = await db.Uploads.SingleAsync(u => u.Id == upload.Id);
        Assert.Equal(["comment", "nps", "segment"], reloaded.Columns);
    }
}