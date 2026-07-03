namespace Pico.Application.Provisioning;

/// <summary>Pluggable interface for resource provisioning. Three implementations:
/// - MockProvisioningBackend (DB-only, no external deps)
/// - DockerProvisioningBackend (creates real Docker containers)
/// - OpenStackProvisioningBackend (calls Nova API)
/// The mode is selected at startup via PROVISIONING_MODE env var.
/// </summary>
public interface IProvisioningBackend
{
    /// <summary>Backend identifier shown in /api/health.</summary>
    string Mode { get; }

    Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct);
    Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct);
    Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct);
    Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct);
    Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct);
    Task<BackendHealth> GetHealthAsync(CancellationToken ct);

    /// <summary>
    /// Open an interactive shell session against the resource identified
    /// by <paramref name="externalId"/>. The returned session pipes the
    /// caller's stdin (via <see cref="IShellSession.InputStream"/>) into
    /// the remote shell and surfaces the remote shell's stdout/stderr
    /// via <see cref="IShellSession.OutputStream"/>. Used by the
    /// in-browser WebSocket shell panel. Always call Dispose or Kill
    /// when finished.
    ///
    /// Throws if the backend doesn't support interactive exec (e.g. the
    /// OpenStack backend may not have a console-attached shell). Backends
    /// that don't support it must return a session that immediately
    /// signals EOF on OutputStream and prints a one-line "shell not
    /// supported" stub on OutputStream.
    /// </summary>
    Task<IShellSession> ExecInteractiveAsync(string externalId, CancellationToken ct);
}

/// <summary>
/// Bidirectional byte stream between the API's WebSocket bridge and a
/// remote shell process on the VM. <see cref="InputStream"/> is written
/// by the API (bytes coming from the browser keystrokes); <see
/// cref="OutputStream"/> is read by the API (bytes to forward to the
/// browser as terminal output). Disposal or <see cref="Kill"/> ends the
/// underlying process and closes both streams.
/// </summary>
public interface IShellSession : IAsyncDisposable
{
    /// <summary>Bytes written here go to the shell's stdin.</summary>
    Stream InputStream { get; }
    /// <summary>Read the shell's stdout/stderr here until EOF (exit).</summary>
    Stream OutputStream { get; }
    /// <summary>Forcibly terminate the shell (e.g. user pressed Ctrl+D).</summary>
    void Kill();
}
