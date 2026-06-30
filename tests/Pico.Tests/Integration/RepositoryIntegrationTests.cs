using Microsoft.EntityFrameworkCore;
using Pico.Domain.Entities;
using Pico.Domain.Enums;
using Pico.Infrastructure.Persistence;
using Pico.Infrastructure.Repositories;
using Pico.Tests.Helpers;
using Xunit;

namespace Pico.Tests.Integration;

/// <summary>
/// Repository tests using a real PostgreSQL container via Testcontainers.
/// Verifies the EF Core schema works against actual Postgres, not just in-memory.
/// </summary>
[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }

[Collection("Postgres")]
public class RepositoryIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fx;
    private PicoDbContext _db = null!;

    public RepositoryIntegrationTests(PostgresFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        await _fx.ResetAsync();
        _db = new PicoDbContext(_fx.BuildOptions());
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UserRepository_AddAndFind_Works()
    {
        var repo = new UserRepository(_db);
        var user = User.Create("alice@example.com", "Alice", "hash", UserRole.Customer);
        await repo.AddAsync(user, CancellationToken.None);

        var found = await repo.FindByEmailAsync("alice@example.com", CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal("alice@example.com", found!.Email);
        Assert.Equal("Alice", found.Name);
        Assert.Equal(UserRole.Customer, found.Role);
    }

    [Fact]
    public async Task UserRepository_DuplicateEmail_ThrowsDbUpdateException()
    {
        var repo = new UserRepository(_db);
        await repo.AddAsync(User.Create("dupe@example.com", "X", "h", UserRole.Customer), CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await repo.AddAsync(User.Create("dupe@example.com", "Y", "h", UserRole.Customer), CancellationToken.None));
    }

    [Fact]
    public async Task FlavorRepository_ListActive_OrdersByPrice()
    {
        var repo = new FlavorRepository(_db);
        await repo.AddAsync(Flavor.Create("expensive", 8, 16384, 100, 1.0m, 720m, "Compute"), CancellationToken.None);
        await repo.AddAsync(Flavor.Create("cheap", 1, 512, 10, 0.005m, 3.0m, "General"), CancellationToken.None);
        await repo.AddAsync(Flavor.Create("disabled", 2, 4096, 50, 0.5m, 360m, "Memory"), CancellationToken.None);

        var flavors = await repo.ListActiveAsync(CancellationToken.None);
        Assert.Equal(2, flavors.Count);
        Assert.Equal("cheap", flavors[0].Name);  // cheapest first
        Assert.Equal("expensive", flavors[1].Name);
    }

    [Fact]
    public async Task ResourceRepository_AddEvent_AppendOnly()
    {
        var user = User.Create("u@e.com", "U", "h", UserRole.Customer);
        var flavor = Flavor.Create("pico.small", 1, 2048, 40, 0.025m, 15m, "General");
        var image = Image.Create("ubuntu-24", "Ubuntu", "24.04", 2);

        var userRepo = new UserRepository(_db);
        var flavorRepo = new FlavorRepository(_db);
        var imageRepo = new ImageRepository(_db);
        var resRepo = new ResourceRepository(_db);

        await userRepo.AddAsync(user, CancellationToken.None);
        await flavorRepo.AddAsync(flavor, CancellationToken.None);
        await imageRepo.AddAsync(image, CancellationToken.None);

        var resource = Resource.Provision(user.Id, flavor.Id, image.Id, "my-vm");
        await resRepo.AddAsync(resource, CancellationToken.None);
        await resRepo.AddEventAsync(
            ResourceEvent.Create(resource.Id, "Created", ResourceStatus.Created, ResourceStatus.Created, "init"),
            CancellationToken.None);

        resource.TransitionTo(ResourceStatus.Provisioning, "started");
        resRepo.Update(resource);
        await resRepo.AddEventAsync(
            ResourceEvent.Create(resource.Id, "StatusChange", ResourceStatus.Created, ResourceStatus.Provisioning, "begin"),
            CancellationToken.None);

        var events = await resRepo.ListEventsAsync(resource.Id, CancellationToken.None);
        Assert.Equal(2, events.Count);
        Assert.Equal(ResourceStatus.Created, events[0].NewStatus);
        Assert.Equal(ResourceStatus.Provisioning, events[1].NewStatus);
    }

    [Fact]
    public async Task ResourceService_Provision_FullLifecycle()
    {
        var user = User.Create("svc@e.com", "S", "h", UserRole.Customer);
        var flavor = Flavor.Create("pico.small", 1, 2048, 40, 0.025m, 15m, "General");
        var image = Image.Create("ubuntu-24", "Ubuntu", "24.04", 2);

        var userRepo = new UserRepository(_db);
        var flavorRepo = new FlavorRepository(_db);
        var imageRepo = new ImageRepository(_db);
        var resRepo = new ResourceRepository(_db);

        await userRepo.AddAsync(user, CancellationToken.None);
        await flavorRepo.AddAsync(flavor, CancellationToken.None);
        await imageRepo.AddAsync(image, CancellationToken.None);

        var backend = new FakeProvisioningBackend();
        var service = new Pico.Application.Resources.ResourceService(resRepo, backend);

        // Provision
        var provisionResult = await service.ProvisionAsync(user.Id,
            new Pico.Application.Resources.ProvisionRequestDto("my-vm", flavor.Id, image.Id),
            CancellationToken.None);
        Assert.True(provisionResult.IsSuccess);
        var resourceId = provisionResult.Value!.Id;

        // Start (walk through transitions first)
        var r = await resRepo.FindByIdAsync(resourceId, CancellationToken.None);
        Assert.NotNull(r);
        r!.TransitionTo(ResourceStatus.Running, "manual");
        resRepo.Update(r);
        r.TransitionTo(ResourceStatus.Stopped, "manual");
        resRepo.Update(r);

        var startResult = await service.StartAsync(resourceId, user.Id, CancellationToken.None);
        Assert.True(startResult.IsSuccess);
        Assert.Equal(ResourceStatus.Running, (await resRepo.FindByIdAsync(resourceId, CancellationToken.None))!.Status);

        // Terminate
        var stopResult = await service.TerminateAsync(resourceId, user.Id, CancellationToken.None);
        Assert.True(stopResult.IsSuccess);
        var final = await resRepo.FindByIdAsync(resourceId, CancellationToken.None);
        Assert.Equal(ResourceStatus.Terminated, final!.Status);
        Assert.True(final.IsTerminated());
    }
}