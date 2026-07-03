using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pico.Application.Provisioning;

namespace Pico.Infrastructure.Provisioning;

public class OpenStackOptions
{
    public string AuthUrl { get; set; } = "http://localhost:5000/v3";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "";
    public string ProjectName { get; set; } = "admin";
    public string Region { get; set; } = "RegionOne";
    /// <summary>Nova compute endpoint URL. If empty, discovered from Keystone service catalog.</summary>
    public string ComputeUrl { get; set; } = "";
    /// <summary>Default Nova flavor ID to use when flavor mapping is not configured.</summary>
    public string DefaultFlavorId { get; set; } = "1";
    /// <summary>Default Nova image ID to use when image mapping is not configured.</summary>
    public string DefaultImageId { get; set; } = "";
}

/// <summary>
/// OpenStack Nova API integration. Calls Nova endpoint to provision actual VMs.
/// Used when PROVISIONING_MODE=openstack.
/// Auth flow: POST to auth_url/auth/tokens → get X-Subject-Token header → use for Nova calls.
/// </summary>
public class OpenStackProvisioningBackend : IProvisioningBackend
{
    public string Mode => "openstack";

    private readonly HttpClient _http;
    private readonly OpenStackOptions _options;
    private readonly ILogger<OpenStackProvisioningBackend> _logger;
    private TokenCache? _tokenCache;
    private string? _computeUrl;

    private record TokenCache(string Token, DateTimeOffset ExpiresAt);
    private record NovaServer(string id, string status);

    public OpenStackProvisioningBackend(IHttpClientFactory httpClientFactory, IOptions<OpenStackOptions> options, ILogger<OpenStackProvisioningBackend> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = httpClientFactory.CreateClient("openstack");
    }

    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct)
    {
        try
        {
            var token = await GetTokenAsync(ct);
            var computeUrl = await GetComputeUrlAsync(token, ct);
            var name = $"pico-{request.Name}-{Guid.NewGuid():N}"[..Math.Min(64, $"pico-{request.Name}-{Guid.NewGuid():N}".Length)];

            var flavorRef = _options.DefaultFlavorId;
            var imageRef = _options.DefaultImageId;

            var server = await CreateServerAsync(token, computeUrl, name, flavorRef, imageRef, ct);
            var ip = await GetServerIpAsync(token, computeUrl, server.id, ct);
            return ProvisionResult.Ok(server.id, ip);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenStack provisioning failed");
            return ProvisionResult.Fail(ex.Message);
        }
    }

    public async Task<ProvisionResult> StartAsync(string externalId, CancellationToken ct)
    {
        try
        {
            var token = await GetTokenAsync(ct);
            var computeUrl = await GetComputeUrlAsync(token, ct);
            var resp = await _http.PostAsync($"{computeUrl}/servers/{externalId}/action",
                new StringContent("{\"os-start\": null}", Encoding.UTF8, "application/json"), ct);
            resp.EnsureSuccessStatusCode();
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex) { return ProvisionResult.Fail(ex.Message); }
    }

    public async Task<ProvisionResult> StopAsync(string externalId, CancellationToken ct)
    {
        try
        {
            var token = await GetTokenAsync(ct);
            var computeUrl = await GetComputeUrlAsync(token, ct);
            var resp = await _http.PostAsync($"{computeUrl}/servers/{externalId}/action",
                new StringContent("{\"os-stop\": null}", Encoding.UTF8, "application/json"), ct);
            resp.EnsureSuccessStatusCode();
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex) { return ProvisionResult.Fail(ex.Message); }
    }

    public async Task<ProvisionResult> TerminateAsync(string externalId, CancellationToken ct)
    {
        try
        {
            var token = await GetTokenAsync(ct);
            var computeUrl = await GetComputeUrlAsync(token, ct);
            var resp = await _http.DeleteAsync($"{computeUrl}/servers/{externalId}", ct);
            resp.EnsureSuccessStatusCode();
            return ProvisionResult.Ok(externalId, "");
        }
        catch (Exception ex) { return ProvisionResult.Fail(ex.Message); }
    }

    public Task<ResourceUsage> GetUsageAsync(string externalId, CancellationToken ct) =>
        Task.FromResult(ResourceUsage.Empty());

    public async Task<BackendHealth> GetHealthAsync(CancellationToken ct)
    {
        try
        {
            var token = await GetTokenAsync(ct);
            var computeUrl = await GetComputeUrlAsync(token, ct);
            var resp = await _http.GetAsync($"{computeUrl}/os-services?binary=nova-compute", ct);
            var ok = resp.IsSuccessStatusCode;
            return new BackendHealth("openstack", ok, ok ? "Nova reachable" : "Nova unreachable", DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new BackendHealth("openstack", false, ex.Message, DateTimeOffset.UtcNow);
        }
    }

    public Task<IShellSession> ExecInteractiveAsync(string externalId, CancellationToken ct)
    {
        // OpenStack shell-into-VM goes through nova-console (VNC) or
        // serial-console. Not implemented yet — the WebSocket endpoint
        // surfaces this error to the client. Returning a session that
        // immediately signals EOF would hide the missing-feature signal,
        // so we throw so the endpoint can return 501 Not Implemented.
        throw new NotSupportedException(
            "Interactive shell is not yet implemented for OpenStack-provisioned VMs.");
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_tokenCache is not null && _tokenCache.ExpiresAt > DateTimeOffset.UtcNow)
            return _tokenCache.Token;

        var authBody = new
        {
            auth = new
            {
                identity = new
                {
                    methods = new[] { "password" },
                    password = new
                    {
                        user = new { name = _options.Username, domain = new { id = "default" }, password = _options.Password }
                    }
                },
                scope = new
                {
                    project = new { name = _options.ProjectName, domain = new { id = "default" } }
                }
            }
        };

        var resp = await _http.PostAsync($"{_options.AuthUrl}/auth/tokens",
            new StringContent(JsonSerializer.Serialize(authBody), Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();

        // Keystone v3 returns the token in the X-Subject-Token header
        var token = resp.Headers.TryGetValues("X-Subject-Token", out var tokens)
            ? tokens.First()
            : throw new InvalidOperationException("Keystone did not return X-Subject-Token header");

        var jsonText = await resp.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(jsonText);
        var expiresAt = DateTimeOffset.Parse(json.RootElement.GetProperty("token").GetProperty("expires_at").GetString()!);
        _tokenCache = new TokenCache(token, expiresAt.AddMinutes(-5));
        return token;
    }

    private async Task<string> GetComputeUrlAsync(string token, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_options.ComputeUrl))
            return _options.ComputeUrl.TrimEnd('/');

        if (_computeUrl is not null) return _computeUrl;

        _http.DefaultRequestHeaders.Remove("X-Auth-Token");
        _http.DefaultRequestHeaders.Add("X-Auth-Token", token);

        var resp = await _http.GetAsync($"{_options.AuthUrl}/auth/catalog", ct);
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        foreach (var catalog in json.RootElement.GetProperty("token").GetProperty("catalog").EnumerateArray())
        {
            if (catalog.GetProperty("type").GetString() == "compute")
            {
                foreach (var endpoint in catalog.GetProperty("endpoints").EnumerateArray())
                {
                    if (endpoint.GetProperty("interface").GetString() == "public")
                    {
                        _computeUrl = endpoint.GetProperty("url").GetString()!.TrimEnd('/');
                        return _computeUrl;
                    }
                }
            }
        }

        throw new InvalidOperationException("No compute endpoint found in Keystone service catalog");
    }

    private async Task<NovaServer> CreateServerAsync(string token, string computeUrl, string name, string flavorRef, string imageRef, CancellationToken ct)
    {
        _http.DefaultRequestHeaders.Remove("X-Auth-Token");
        _http.DefaultRequestHeaders.Add("X-Auth-Token", token);
        var body = new { server = new { name = name, flavorRef = flavorRef, imageRef = imageRef, min = 1, max = 1 } };
        var resp = await _http.PostAsync($"{computeUrl}/servers",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var server = json.RootElement.GetProperty("server");
        return new NovaServer(server.GetProperty("id").GetString()!, server.GetProperty("status").GetString()!);
    }

    private async Task<string> GetServerIpAsync(string token, string computeUrl, string serverId, CancellationToken ct)
    {
        await Task.Delay(2000, ct);
        _http.DefaultRequestHeaders.Remove("X-Auth-Token");
        _http.DefaultRequestHeaders.Add("X-Auth-Token", token);
        var resp = await _http.GetAsync($"{computeUrl}/servers/{serverId}", ct);
        if (!resp.IsSuccessStatusCode) return "127.0.0.1";
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var addresses = json.RootElement.GetProperty("server").GetProperty("addresses");
        foreach (var network in addresses.EnumerateObject())
        {
            foreach (var addr in network.Value.EnumerateArray())
            {
                var osExt = addr.TryGetProperty("OS-EXT-IPS:type", out var t) ? t.GetString() : null;
                if (osExt == "floating") return addr.GetProperty("addr").GetString()!;
            }
        }
        // Fallback: first available address
        foreach (var network in addresses.EnumerateObject())
        {
            foreach (var addr in network.Value.EnumerateArray())
            {
                return addr.GetProperty("addr").GetString()!;
            }
        }
        return "127.0.0.1";
    }
}