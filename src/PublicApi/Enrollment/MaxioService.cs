using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Enrollment;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioService(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    private string BaseUrl => !string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? _settings.BaseUrl!.TrimEnd('/')
        : $"https://{_settings.Subdomain}.chargify.com";

    private void ConfigureAuth()
    {
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
    }

    public async Task<JsonObject?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        ConfigureAuth();
        var url = $"{BaseUrl}/customers/lookup.json?reference={System.Uri.EscapeDataString(reference)}";
        var resp = await _http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return JsonObject.Create(doc.RootElement);
    }

    public async Task<JsonObject?> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken ct = default)
    {
        ConfigureAuth();
        var body = new { customer = new { first_name = firstName, last_name = lastName, email, reference } };
        var json = JsonSerializer.Serialize(body);
        var resp = await _http.PostAsync($"{BaseUrl}/customers.json", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return JsonObject.Create(doc.RootElement);
    }

    public async Task<JsonObject?> ListFamilyProductsAsync(CancellationToken ct = default)
    {
        ConfigureAuth();
        var url = $"{BaseUrl}/product_families/handle:{_settings.ProductFamilyHandle}/products.json?per_page=50";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return JsonObject.Create(doc.RootElement);
    }

    public async Task<JsonObject?> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken ct = default)
    {
        ConfigureAuth();
        var body = new { subscription = new { product_handle = productHandle, customer_reference = customerReference } };
        var json = JsonSerializer.Serialize(body);
        var resp = await _http.PostAsync($"{BaseUrl}/subscriptions.json", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return JsonObject.Create(doc.RootElement);
    }

    public async Task<JsonObject?> ListSubscriptionsByCustomerReferenceAsync(string reference, CancellationToken ct = default)
    {
        ConfigureAuth();
        var url = $"{BaseUrl}/subscriptions.json?customer_reference={System.Uri.EscapeDataString(reference)}&per_page=50";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return JsonObject.Create(doc.RootElement);
    }
}
