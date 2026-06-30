using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pico.Infrastructure.Persistence;
using Pico.Infrastructure.Seed;

namespace Pico.Infrastructure;

/// <summary>
/// IHostedService: at startup, run migrations + (optionally) seed demo data.
/// Controlled by Pico:Database:AutoMigrate and Pico:Database:SeedDemoData flags.
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

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PicoDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var autoMigrate = config.GetValue<bool>("Database:AutoMigrate", false);
        var seedDemo = config.GetValue<bool>("Database:SeedDemoData", false);

        if (autoMigrate)
        {
            _logger.LogInformation("Running EF migrations...");
            await db.Database.MigrateAsync(ct);
            _logger.LogInformation("Migrations complete");
        }
        else
        {
            _logger.LogInformation("AutoMigrate=false — skipping migrations (use Database.MigrateAtStartup=true or run dotnet ef database update)");
            await db.Database.EnsureCreatedAsync(ct);
        }

        if (seedDemo)
        {
            _logger.LogInformation("Seeding demo data...");
            await seeder.SeedAsync(ct);
            _logger.LogInformation("Demo data seed complete");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
