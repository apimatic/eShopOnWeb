using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioService(HttpClient http, IOptions<MaxioSettings> options)
    {
        _http = http;
        _settings = options.Value;
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
    }

    public async Task<JsonNode?> GetProductsAsync(string familyHandle, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("products.json", ct);
        resp.EnsureSuccessStatusCode();
        var arr = await resp.Content.ReadFromJsonAsync<JsonNode>(ct);
        return arr;
    }

    public async Task<JsonNode?> GetCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    public async Task<JsonNode?> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["reference"] = reference,
                ["first_name"] = firstName,
                ["last_name"] = lastName,
                ["email"] = email
            }
        };
        var resp = await _http.PostAsJsonAsync("customers.json", body, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    public async Task<JsonNode?> GetCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"customers/{customerId}/subscriptions.json", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    public async Task<JsonNode?> CreateSubscriptionAsync(string productHandle, string customerReference, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = productHandle,
                ["customer_reference"] = customerReference
            }
        };
        var resp = await _http.PostAsJsonAsync("subscriptions.json", body, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonNode>(ct);
    }
}
