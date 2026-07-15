using Microsoft.EntityFrameworkCore;

using Npgsql;

using Testcontainers.PostgreSql;

using VerbatimIntelligence.Api.Auth;
using VerbatimIntelligence.Api.Data;

namespace VerbatimIntelligence.Api.Tests;

/// <summary>
/// O5 (docs/security-review.md): the application connects with a non-superuser
/// role. This migrates a throwaway database as the owner, then runs the real
/// db-init.sql — the artifact shipped to prod, mounted at /opt/db-init.sql — and
/// proves verbatim_app can do the app's DML yet is denied every superuser
/// gesture. Widening the grants or dropping the role split fails this test.
/// </summary>
public sealed class DatabaseLeastPrivilegeTests : IAsyncLifetime
{
    // Only ever exists inside a throwaway container.
    private const string AppPassword = "o5-least-privilege-test";
    // Read from the read-only mount (compose.yaml), run from inside the container.
    private const string HostScriptPath = "/opt/db-init.sql";
    private const string ContainerScriptPath = "/tmp/db-init.sql";

    // Username/database mirror production so db-init.sql's owner role
    // (ALTER DEFAULT PRIVILEGES FOR ROLE verbatim) resolves unchanged.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "postgres:18-alpine@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15")
        .WithUsername("verbatim")
        .WithDatabase("verbatim")
        // db-init.sql reads the app password with \getenv, off the command line —
        // the test provisions it the same way, through the environment.
        .WithEnvironment("APP_DB_PASSWORD", AppPassword)
        .Build();

    private string AppConnectionString
    {
        get
        {
            var admin = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
            {
                Username = "verbatim_app",
                Password = AppPassword,
                // A fresh physical connection per open: no admin connection is
                // reused with the app role's credentials.
                Pooling = false,
            };
            return admin.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Schema first, as the owning superuser — exactly like the prod migrate step.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var db = new AppDbContext(options, new CurrentAccountAccessor()))
        {
            await db.Database.MigrateAsync();
        }

        // Then the real role-provisioning script, through psql, as prod runs it.
        var script = await File.ReadAllBytesAsync(HostScriptPath);
        await _postgres.CopyAsync(script, ContainerScriptPath);
        var result = await _postgres.ExecAsync(
        [
            "psql", "-v", "ON_ERROR_STOP=1",
            "-U", "verbatim", "-d", "verbatim", "-f", ContainerScriptPath,
        ]);
        Assert.True(result.ExitCode == 0, result.Stderr);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task AppRole_CanDoTheApplicationsDml()
    {
        await using var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync();

        // Read, and the row-lock path (ShareEndpoints) that needs UPDATE —
        // both throw if the grant is missing, which is the point.
        await Exec(connection, "SELECT count(*) FROM analyses");
        await Exec(connection, "SELECT id FROM analyses LIMIT 1 FOR UPDATE");

        // Write: insert, update, delete a row of its own, asserting each took.
        Assert.Equal(1, await Exec(connection,
            "INSERT INTO users (id, email, created_at) "
            + "VALUES (gen_random_uuid(), 'o5@test.example', now())"));
        Assert.Equal(1, await Exec(connection,
            "UPDATE users SET email = email WHERE email = 'o5@test.example'"));
        Assert.Equal(1, await Exec(connection,
            "DELETE FROM users WHERE email = 'o5@test.example'"));
    }

    [Fact]
    public async Task AppRole_IsNotASuperuser()
    {
        await using var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT rolsuper FROM pg_roles WHERE rolname = 'verbatim_app'", connection);
        Assert.False((bool)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task AppRole_IsDeniedEverySuperuserGesture()
    {
        await using var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync();

        // DDL: cannot create tables in the schema it only reads/writes.
        await Assert.ThrowsAsync<PostgresException>(
            () => Exec(connection, "CREATE TABLE o5_intruder (id int)"));
        // Server-side program execution: superuser-only, the classic escalation.
        await Assert.ThrowsAsync<PostgresException>(
            () => Exec(connection, "COPY (SELECT 1) TO PROGRAM 'true'"));
        // Reading raw credentials: pg_authid is superuser-only.
        await Assert.ThrowsAsync<PostgresException>(
            () => Exec(connection, "SELECT rolpassword FROM pg_authid LIMIT 1"));
    }

    private static async Task<int> Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }
}