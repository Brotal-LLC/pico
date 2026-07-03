using Pico.Application.Provisioning;
using Pico.Tests.Helpers;

namespace Pico.Tests.Unit;

/// <summary>
/// Verifies the contract for IProvisioningBackend.ExecInteractiveAsync:
///   - Returns an IShellSession that pipes bytes between caller and the
///     remote shell (Docker exec /bin/sh by default).
///   - The InputStream is writable from the caller's side; the
///     OutputStream is readable.
///   - On Dispose / Kill, the underlying process terminates.
///   - Mock backend returns a session whose output is a friendly stub
///     message so the WebSocket layer has something to display even when
///     Docker mode is not active (dev reviewers, demo accounts).
/// </summary>
public class InteractiveExecTests
{
    [Fact]
    public async Task MockBackend_ExecInteractiveAsync_ReturnsReadableSession()
    {
        var backend = new FakeProvisioningBackend();

        await using var session = await backend.ExecInteractiveAsync("fake-container", CancellationToken.None);

        Assert.NotNull(session);
        Assert.NotNull(session.OutputStream);
        // Mock session echoes input back to output (deterministic for tests).
        var input = new byte[] { 0x68, 0x65, 0x6c, 0x6c, 0x6f }; // "hello"
        await session.InputStream.WriteAsync(input, CancellationToken.None);
        session.InputStream.Close();
        var output = new byte[input.Length];
        var read = 0;
        while (read < output.Length)
        {
            var n = await session.OutputStream.ReadAsync(output.AsMemory(read), CancellationToken.None);
            if (n == 0) break;
            read += n;
        }
        Assert.Equal(input, output);
    }

    [Fact]
    public async Task MockBackend_ExecInteractiveAsync_TracksCallCount()
    {
        var backend = new FakeProvisioningBackend();

        await using var s1 = await backend.ExecInteractiveAsync("a", CancellationToken.None);
        await using var s2 = await backend.ExecInteractiveAsync("b", CancellationToken.None);

        Assert.Equal(2, backend.ExecInteractiveCalls);
        Assert.Equal(new[] { "a", "b" }, backend.ExecInteractiveTargets);
    }

    [Fact]
    public async Task MockBackend_ExecInteractiveAsync_KillStopsSession()
    {
        var backend = new FakeProvisioningBackend();

        var session = await backend.ExecInteractiveAsync("c", CancellationToken.None);
        // Before Kill, OutputStream should be open. After Kill, it should
        // return 0 bytes (EOF) so the WebSocket knows the session ended.
        session.Kill();
        var buf = new byte[16];
        var n = await session.OutputStream.ReadAsync(buf, CancellationToken.None);
        Assert.Equal(0, n);
    }
}