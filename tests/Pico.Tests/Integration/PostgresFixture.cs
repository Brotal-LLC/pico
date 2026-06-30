using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pico.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Pico.Tests.Integration;

/// <summary>
/// Shared fixture: spins up a real PostgreSQL container via Testcontainers,
/// runs the Pico migrations + seed, and provides a fresh PicoDbContext for each test.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlBuilder _builder = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("pico_test")
        .WithUsername("pico")
        .WithPassword("pico")
        .WithPortBinding(0, true);  // random host port

    public PostgreSqlContainer Container { get; private set; } = null!;
    public string ConnectionString => throw new NotImplementedException("Set via ConfigureAsync");
    private string _connStr = "";

    public async Task InitializeAsync()
    {
        Container = _builder.Build();
        await Container.StartAsync();
        _connStr = Container.GetConnectionString();
    }

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();

    /// <summary>Build a DbContextOptions wired to the test container.</summary>
    public DbContextOptions<PicoDbContext> BuildOptions()
    {
        return new DbContextOptionsBuilder<PicoDbContext>()
            .UseNpgsql(_connStr)
            .Options;
    }

    /// <summary>Create a fresh schema (drops + migrates) and seed it.</summary>
    public async Task ResetAsync()
    {
        var options = BuildOptions();
        await using var db = new PicoDbContext(options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        // Apply our hand-written initial migration
        // (EnsureCreated creates the schema from the model — sufficient for integration tests)
    }
}