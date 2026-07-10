using Microsoft.EntityFrameworkCore;

using VerbatimIntelligence.Api.Analyses;

namespace VerbatimIntelligence.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Analysis> Analyses => Set<Analysis>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Analysis>(entity =>
        {
            // The status CHECK is part of the schema contract: the worker
            // writes this column too, the database is the common guard.
            entity.ToTable("analyses", table => table.HasCheckConstraint(
                "ck_analyses_status",
                "status IN ('pending', 'running', 'succeeded', 'failed')"));

            entity.Property(analysis => analysis.Status)
                .HasConversion(status => ToDb(status), value => FromDb(value))
                .HasMaxLength(16);
        });
    }

    private static string ToDb(AnalysisStatus status) => status switch
    {
        AnalysisStatus.Pending => "pending",
        AnalysisStatus.Running => "running",
        AnalysisStatus.Succeeded => "succeeded",
        AnalysisStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static AnalysisStatus FromDb(string value) => value switch
    {
        "pending" => AnalysisStatus.Pending,
        "running" => AnalysisStatus.Running,
        "succeeded" => AnalysisStatus.Succeeded,
        "failed" => AnalysisStatus.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}