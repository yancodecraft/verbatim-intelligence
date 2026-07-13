using Microsoft.EntityFrameworkCore;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Auth;

namespace VerbatimIntelligence.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Analysis> Analyses => Set<Analysis>();

    public DbSet<User> Users => Set<User>();

    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();

    public DbSet<Session> Sessions => Set<Session>();

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

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.Property(user => user.Email).HasMaxLength(320);
            entity.HasIndex(user => user.Email).IsUnique();
        });

        modelBuilder.Entity<LoginToken>(entity =>
        {
            entity.ToTable("login_tokens");
            entity.Property(token => token.TokenHash).HasMaxLength(64);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasOne<User>().WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.Property(session => session.TokenHash).HasMaxLength(64);
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.HasOne<User>().WithMany()
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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