using Microsoft.EntityFrameworkCore;
using Npgsql;
using RecipePlanner.Api.Data;
using Testcontainers.PostgreSql;

namespace RecipePlanner.Tests;

// The tests run on real Postgres, the same database as production, so foreign keys, column sizes, unique
// indexes and SQL translation are all tested. (EF's in-memory provider ignores those, which once hid three bugs.)
//
// Where the server comes from:
//   - TEST_POSTGRES set: that server, e.g. "Host=localhost;Port=5432;Username=postgres;Password=..."
//   - otherwise a throwaway postgres:16 container, started once per test run (needs Docker running)
//
// Each test factory gets its own database, copied from a template that has every migration applied.
// Copying a template takes milliseconds, so tests stay isolated without re-running migrations each time.
public static class TestDatabase
{
    private const string Prefix = "rptest_";
    private const string Template = Prefix + "template";

    private static readonly Lazy<Task<string>> Server = new(StartServerAsync);
    private static readonly SemaphoreSlim CreateLock = new(1, 1);
    private static readonly HashSet<string> Created = [];

    /// <summary>Connection string for the test database with this name, creating it on first use</summary>
    public static string ConnectionString(string name) => ConnectionStringAsync(name).GetAwaiter().GetResult();

    public static async Task<string> ConnectionStringAsync(string name)
    {
        var server = await Server.Value;
        var database = Prefix + new string(name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        if (database.Length > 63) database = database[..63]; // Postgres's limit on names

        await CreateLock.WaitAsync();
        try
        {
            if (Created.Add(database))
            {
                await using var admin = new NpgsqlConnection(server);
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"""CREATE DATABASE "{database}" TEMPLATE "{Template}" """, admin);
                await create.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            CreateLock.Release();
        }

        return new NpgsqlConnectionStringBuilder(server)
        {
            Database = database,
            // Hundreds of short-lived test databases: keep each pool small and close idle connections quickly,
            // or the server runs out of connections partway through a run
            MaxPoolSize = 10,
            ConnectionIdleLifetime = 2,
            ConnectionPruningInterval = 1
        }.ConnectionString;
    }

    private static async Task<string> StartServerAsync()
    {
        var server = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(server))
        {
            var container = new PostgreSqlBuilder("postgres:16-alpine")
                // Speed over durability (it's thrown away), and room for many test databases at once
                .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "max_connections=500")
                .Build();
            await container.StartAsync(); // Testcontainers removes it when the test run ends
            server = container.GetConnectionString();
        }

        await using (var admin = new NpgsqlConnection(server))
        {
            await admin.OpenAsync();
            // A shared server (TEST_POSTGRES) may still have databases from an earlier run
            var leftovers = new List<string>();
            await using (var list = new NpgsqlCommand($"SELECT datname FROM pg_database WHERE datname LIKE '{Prefix}%'", admin))
            await using (var reader = await list.ExecuteReaderAsync())
                while (await reader.ReadAsync()) leftovers.Add(reader.GetString(0));
            foreach (var old in leftovers)
            {
                await using var drop = new NpgsqlCommand($"""DROP DATABASE "{old}" WITH (FORCE)""", admin);
                await drop.ExecuteNonQueryAsync();
            }
            await using var create = new NpgsqlCommand($"""CREATE DATABASE "{Template}" """, admin);
            await create.ExecuteNonQueryAsync();
        }

        // Every migration, exactly as production applies them
        var templateConnection = new NpgsqlConnectionStringBuilder(server) { Database = Template, Pooling = false }.ConnectionString;
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(templateConnection).Options))
            await db.Database.MigrateAsync();

        return server;
    }
}
