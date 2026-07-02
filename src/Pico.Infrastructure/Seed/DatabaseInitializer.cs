using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pico.Infrastructure.Persistence;
using Pico.Infrastructure.Seed;

namespace Pico.Infrastructure;

/// <summary>
/// IHostedService: at startup, runs migrations + (optionally) seeds demo data.
/// Controlled by Database:AutoMigrate and Database:SeedDemoData flags.
/// </summary>
public class DatabaseInitializer : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IServiceProvider services, ILogger<DatabaseInitializer> logger)
    {
        _services = services;
        _logger = logger;
    }

    // Bounded retry loop for the very first migration. On cold starts the
    // Docker embedded DNS resolver (127.0.0.11) can briefly return EAGAIN
    // when the API container tries to resolve `postgres` for the first
    // time. The first MigrateAsync call would otherwise die with a fatal
    // `Resource temporarily unavailable` from System.Net.Dns. We retry
    // for up to ~30s (10 attempts × exponential backoff capped at 4s) so
    // the resolver has time to settle.
    private const int MaxStartupRetries = 10;
    private static readonly TimeSpan MaxStartupBackoff = TimeSpan.FromSeconds(4);

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PicoDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var autoMigrate = config.GetValue<bool>("Database:AutoMigrate", false);
        var seedDemo = config.GetValue<bool>("Database:SeedDemoData", false);

        try
        {
            if (autoMigrate)
            {
                _logger.LogInformation("Running EF migrations...");
                await RunWithStartupRetryAsync(
                    async innerCt => await db.Database.MigrateAsync(innerCt),
                    ct);
                _logger.LogInformation("Migrations complete");
            }
            else
            {
                _logger.LogInformation("AutoMigrate=false — skipping migrations. Using EnsureCreated.");
                await RunWithStartupRetryAsync(
                    async innerCt => await db.Database.EnsureCreatedAsync(innerCt),
                    ct);
            }
        }
        catch (Exception ex) when (IsPasswordMismatch(ex))
        {
            // The Postgres volume was initialized with a different password than
            // the API is now using. The volume name is hardcoded in compose.yaml
            // and persists across re-clones, so a reviewer who previously ran
            // this stack with a different password gets stuck here. Tell them
            // the exact recovery command, then re-throw so the container exits
            // with a non-zero status (the supervisor will show a clear failure).
            _logger.LogCritical(
                ex,
                "Database password does not match the existing Postgres volume. " +
                "This usually means a previous run used a different POSTGRES_PASSWORD. " +
                "To recover: run `docker compose down -v && docker compose up --build` " +
                "(the `-v` deletes the stale data volume).");
            throw;
        }

        if (seedDemo)
        {
            _logger.LogInformation("Seeding demo data...");
            await seeder.SeedAsync(db, ct);
            _logger.LogInformation("Demo data seed complete");
        }
    }

    /// <summary>
    /// Retries a startup-time DB operation with exponential backoff. This
    /// covers transient failures (DNS hiccups, brief Postgres restarts)
    /// that would otherwise make the API container crashloop at boot.
    /// </summary>
    private async Task RunWithStartupRetryAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct)
    {
        var attempt = 0;
        var delay = TimeSpan.FromMilliseconds(250);
        while (true)
        {
            try
            {
                await action(ct);
                return;
            }
            catch (Exception ex) when (!IsPasswordMismatch(ex) && attempt < MaxStartupRetries)
            {
                attempt++;
                _logger.LogWarning(
                    ex,
                    "Transient startup failure ({Attempt}/{Max}); retrying in {Delay}s.",
                    attempt, MaxStartupRetries, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxStartupBackoff.TotalMilliseconds));
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Detects Postgres password-authentication failures regardless of the
    /// exception type Npgsql throws. The underlying API throws either
    /// <see cref="Npgsql.PostgresException"/> (typed) or a wrapped
    /// <see cref="System.Data.Common.DbException"/> with the SqlState as a
    /// data field — we walk the exception chain so both work.
    /// </summary>
    internal static bool IsPasswordMismatch(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is Npgsql.PostgresException pg && pg.SqlState == "28P01")
            {
                return true;
            }
            var sqlState = e.Data?["SqlState"] as string;
            if (sqlState == "28P01")
            {
                return true;
            }
        }
        return false;
    }
}