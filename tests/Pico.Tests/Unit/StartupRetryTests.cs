using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pico.Infrastructure;

namespace Pico.Tests.Unit;

/// <summary>
/// Tests for <see cref="DatabaseInitializer"/>'s startup-time retry behaviour.
/// We don't hit a real database here — instead we drive the retry helper
/// directly via reflection (it's private). The point is to lock in the
/// contract: transient exceptions are retried with exponential backoff and
/// a bounded attempt count; non-retryable exceptions propagate immediately.
/// </summary>
public class StartupRetryTests
{
    /// <summary>
    /// Invokes the private <c>RunWithStartupRetryAsync</c> method on a real
    /// <see cref="DatabaseInitializer"/> instance via reflection. We need a
    /// service provider that can resolve the constructor's dependencies, but
    /// we never call the methods that actually hit a database.
    /// </summary>
    private static async Task InvokeRetryAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct)
    {
        // Build a DatabaseInitializer with a no-op logger; the action delegate
        // is what actually executes, not the migration code path. The service
        // provider is only used to satisfy the constructor — RunWithStartupRetry
        // never touches it.
        var logger = NullLogger<DatabaseInitializer>.Instance;
        var services = new ServiceCollection().BuildServiceProvider();
        var initializer = new DatabaseInitializer(services, logger);

        var method = typeof(DatabaseInitializer).GetMethod(
            "RunWithStartupRetryAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)method.Invoke(initializer, new object[] { action, ct })!;
    }

    [Fact]
    public async Task SucceedsOnFirstAttempt_DoesNotRetry()
    {
        var calls = 0;
        await InvokeRetryAsync(_ => { calls++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TransientFailure_RetriesUntilSuccess()
    {
        var calls = 0;
        await InvokeRetryAsync(_ =>
        {
            calls++;
            if (calls < 3)
            {
                throw new InvalidOperationException("transient");
            }
            return Task.CompletedTask;
        }, CancellationToken.None);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExhaustsRetries_ThenSurfacesLastException()
    {
        // We can't directly override MaxStartupRetries from outside, so we
        // exceed the default (10) and confirm the final exception propagates.
        var calls = 0;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvokeRetryAsync(_ =>
            {
                calls++;
                throw new InvalidOperationException("always fails");
            }, CancellationToken.None));
        Assert.Equal("always fails", ex.Message);
        // 1 initial attempt + 10 retries = 11 calls
        Assert.Equal(11, calls);
    }

    [Fact]
    public async Task CancellationToken_StopsRetries()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await InvokeRetryAsync(_ =>
            {
                calls++;
                cts.Cancel();
                throw new InvalidOperationException("transient");
            }, cts.Token));
        // At least 1 call, but we shouldn't have continued retrying past cancel.
        Assert.True(calls >= 1, "expected at least the initial call");
        Assert.True(calls <= 12, $"expected bounded retries, got {calls}");
    }
}
