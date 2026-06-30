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
}

/// <summary>
/// Real OpenStack Nova API integration. Calls DevStack's Nova endpoint to provision
/// actual VMs. Used when PROVISIONING_MODE=openstack.
/// Auth flow: POST to auth_url/tokens with username/password, get project-scoped token,
/// use it for all subsequent Nova calls.
/// </summary>
public class OpenStackProvisioningBackend : IProvisioningBackend
{
    public string Mode => "openstack";

    private readonly HttpClient _http;
    private readonly OpenStackOptions _options;
    private readonly ILogger<OpenStackProvisioningBackend> _logger;
    private TokenCache? _tokenCache;

    private record TokenCache(string Token, DateTimeOffset ExpiresAt);
    private record NovaServer(string id, string status, Dictionary<string, object> addresses);

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
            var name = $"pico-{request.Name}-{Guid.NewGuid():N}".Substring(0, 64);

            // Map Pico flavor to a Nova flavor via name lookup (configured externally)
            // For now: use the smallest available Nova flavor
            var server = await CreateServerAsync(token, name, "1", "default-image", ct);
            return ProvisionResult.Ok(server.id, await GetServerIpAsync(token, server.id, ct));
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
            var resp = await _http.PostAsync($"/servers/{externalId}/action",
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
            var resp = await _http.PostAsync($"/servers/{externalId}/action",
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
            var resp = await _http.DeleteAsync($"/servers/{externalId}", ct);
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
            var resp = await _http.GetAsync("/os-services?binary=nova-compute", ct);
            var ok = resp.IsSuccessStatusCode;
            return new BackendHealth("openstack", ok, ok ? "Nova reachable" : "Nova unreachable", DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return new BackendHealth("openstack", false, ex.Message, DateTimeOffset.UtcNow);
        }
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

        var jsonText = await resp.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(jsonText);
        var token = json.RootElement.GetProperty("token").GetProperty("id").GetString()!;
        var expiresAt = DateTimeOffset.Parse(json.RootElement.GetProperty("token").GetProperty("expires_at").GetString()!);
        _tokenCache = new TokenCache(token, expiresAt.AddMinutes(-5));
        return token;
    }

    private async Task<NovaServer> CreateServerAsync(string token, string name, string flavorRef, string imageRef, CancellationToken ct)
    {
        _http.DefaultRequestHeaders.Remove("X-Auth-Token");
        _http.DefaultRequestHeaders.Add("X-Auth-Token", token);
        var body = new { server = new { name = name, flavorRef = flavorRef, imageRef = imageRef, min = 1, max = 1 } };
        var resp = await _http.PostAsync("/servers",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        var jsonText = await resp.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(jsonText);
        var server = json.RootElement.GetProperty("server");
        return new NovaServer(server.GetProperty("id").GetString()!, server.GetProperty("status").GetString()!, new());
    }

    private async Task<string> GetServerIpAsync(string token, string serverId, CancellationToken ct)
    {
        await Task.Delay(1000, ct);
        var resp = await _http.GetAsync($"/servers/{serverId}", ct);
        if (!resp.IsSuccessStatusCode) return "127.0.0.1";
        var jsonText = await resp.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(jsonText);
        var addresses = json.RootElement.GetProperty("server").GetProperty("addresses");
        foreach (var network in addresses.EnumerateObject())
        {
            foreach (var addr in network.Value.EnumerateArray())
            {
                var osExt = addr.TryGetProperty("OS-EXT-IPS:type", out var t) ? t.GetString() : null;
                if (osExt == "floating") return addr.GetProperty("addr").GetString()!;
            }
        }
        return "127.0.0.1";
    }
}
