using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingClient
{
    Task<JsonObject?> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<JsonObject?> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken ct = default);
    Task<JsonObject?> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken ct = default);
    Task<JsonObject?> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken ct = default);
    Task<JsonObject?> ListProductsForFamilyHandleAsync(string familyHandle, CancellationToken ct = default);
}

public class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioBillingClient(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var url = BuildUrl(path);
        var req = new HttpRequestMessage(method, url);
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    private string BuildUrl(string path)
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return $"{_settings.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        return $"https://{_settings.Subdomain}.chargify.com/{path.TrimStart('/')}";
    }

    public async Task<JsonObject?> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var req = CreateRequest(HttpMethod.Get, $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        return doc;
    }

    public async Task<JsonObject?> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["first_name"] = firstName,
                ["last_name"] = lastName,
                ["email"] = email,
                ["reference"] = reference
            }
        };
        var req = CreateRequest(HttpMethod.Post, "customers.json");
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
    }

    public async Task<JsonObject?> CreateSubscriptionAsync(string productHandle, int customerId, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = productHandle,
                ["customer_id"] = customerId
            }
        };
        var req = CreateRequest(HttpMethod.Post, "subscriptions.json");
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
    }

    public async Task<JsonObject?> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken ct = default)
    {
        var req = CreateRequest(HttpMethod.Get, $"customers/{customerId}/subscriptions.json");
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
    }

    public async Task<JsonObject?> ListProductsForFamilyHandleAsync(string familyHandle, CancellationToken ct = default)
    {
        var req = CreateRequest(HttpMethod.Get, $"product_families/handle:{familyHandle}/products.json");
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
    }
}
