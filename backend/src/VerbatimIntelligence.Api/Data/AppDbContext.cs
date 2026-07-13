using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using VerbatimIntelligence.Api.Analyses;
using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Uploads;

namespace VerbatimIntelligence.Api.Data;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    CurrentAccountAccessor currentAccount) : DbContext(options)
{
    // Evaluated per query by the global filters below (see practices.md,
    // "scoping mécanique"): with an account resolved, scoped entities are
    // filtered to it — a handler cannot forget. Without one (migrations,
    // health, schema tests), filters are open: those paths carry no
    // caller-supplied ids.
    private Guid? CurrentUserId => currentAccount.UserId;

    public DbSet<Analysis> Analyses => Set<Analysis>();

    public DbSet<Verbatim> Verbatims => Set<Verbatim>();

    public DbSet<Theme> Themes => Set<Theme>();

    public DbSet<ThemeVerbatim> ThemeVerbatims => Set<ThemeVerbatim>();

    public DbSet<Upload> Uploads => Set<Upload>();

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

            // Denormalized from the source upload. Database defaults keep both
            // the deployed N-1 backend (which does not write them yet) and the
            // worker's minimal INSERTs valid through the migration — the
            // expand step of expand/contract (see docs/architecture.md).
            entity.Property(analysis => analysis.SourceFilename)
                .HasMaxLength(255)
                .HasDefaultValue("");
            entity.Property(analysis => analysis.VerbatimCount)
                .HasDefaultValue(0);

            // Pipeline columns, worker-written; defaults keep the deployed N-1
            // backend inserting valid rows through the migration (expand).
            entity.Property(analysis => analysis.Attempts).HasDefaultValue(0);
            entity.Property(analysis => analysis.ProcessedCount).HasDefaultValue(0);
            entity.Property(analysis => analysis.InputTokens).HasDefaultValue(0L);
            entity.Property(analysis => analysis.OutputTokens).HasDefaultValue(0L);

            entity.HasOne<User>().WithMany()
                .HasForeignKey(analysis => analysis.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(analysis =>
                CurrentUserId == null || analysis.UserId == CurrentUserId);
        });

        modelBuilder.Entity<Verbatim>(entity =>
        {
            entity.ToTable("verbatims");

            // Part of the Analysis aggregate: no independent query filter, it
            // is only ever reached through its (already scoped) analysis.
            entity.HasOne<Analysis>().WithMany()
                .HasForeignKey(verbatim => verbatim.AnalysisId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Theme>(entity =>
        {
            entity.ToTable("themes");
            entity.Property(theme => theme.Name).HasMaxLength(200);

            // Part of the Analysis aggregate: no independent query filter, it
            // is only ever reached through its (already scoped) analysis.
            entity.HasOne<Analysis>().WithMany()
                .HasForeignKey(theme => theme.AnalysisId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ThemeVerbatim>(entity =>
        {
            entity.ToTable("theme_verbatims");

            // The composite key doubles as the guard against attaching the
            // same verbatim to a theme twice.
            entity.HasKey(tv => new { tv.ThemeId, tv.VerbatimId });

            entity.HasOne<Theme>().WithMany()
                .HasForeignKey(tv => tv.ThemeId)
                .OnDelete(DeleteBehavior.Cascade);

            // The fidelity invariant lives here: a citation must reference an
            // existing verbatim row, the database refuses anything else.
            entity.HasOne<Verbatim>().WithMany()
                .HasForeignKey(tv => tv.VerbatimId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Upload>(entity =>
        {
            entity.ToTable("uploads");
            entity.Property(upload => upload.Filename).HasMaxLength(255);
            entity.Property(upload => upload.Columns)
                .HasColumnType("jsonb")
                .HasConversion(ColumnsConverter, ColumnsComparer);

            entity.HasOne<User>().WithMany()
                .HasForeignKey(upload => upload.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Same mechanical scoping as analyses: an upload is invisible
            // outside its account before a query even looks (see practices.md).
            entity.HasQueryFilter(upload =>
                CurrentUserId == null || upload.UserId == CurrentUserId);
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

    // Columns are stored as a jsonb string array: read and written whole,
    // never queried, so a JSON document is enough (no relational modelling).
    private static readonly ValueConverter<IReadOnlyList<string>, string> ColumnsConverter =
        new(
            columns => JsonSerializer.Serialize(columns, (JsonSerializerOptions?)null),
            json => JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null)
                ?? new List<string>());

    private static readonly ValueComparer<IReadOnlyList<string>> ColumnsComparer =
        new(
            (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
            columns => columns.Aggregate(0, (hash, value) => HashCode.Combine(hash, value.GetHashCode(StringComparison.Ordinal))),
            columns => columns.ToList());

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