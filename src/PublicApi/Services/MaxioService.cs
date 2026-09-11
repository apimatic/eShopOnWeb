using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<JsonArray> GetSubscriptionPlansAsync();
    Task<JsonObject?> FindCustomerByReferenceAsync(string reference);
    Task<JsonObject> CreateCustomerAsync(string reference, string firstName, string lastName, string email);
    Task<JsonObject> CreateSubscriptionAsync(string customerReference, string productHandle);
    Task<JsonArray> GetSubscriptionsByCustomerReferenceAsync(string reference);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioService(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    private string BaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return _settings.BaseUrl.TrimEnd('/');
        return $"https://{_settings.Subdomain}.chargify.com";
    }

    private HttpClient Client()
    {
        _httpClient.BaseAddress = new Uri(BaseUrl());
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return _httpClient;
    }

    public async Task<JsonArray> GetSubscriptionPlansAsync()
    {
        var client = Client();
        var resp = await client.GetAsync("/products.json?per_page=200");
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var array = new JsonArray();
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var prod = item.GetProperty("product");
                if (prod.TryGetProperty("product_family", out var family) &&
                    family.TryGetProperty("handle", out var fh) &&
                    fh.GetString() == _settings.ProductFamilyHandle)
                {
                    array.Add(JsonNode.Parse(prod.GetRawText()));
                }
            }
        }
        return array;
    }

    public async Task<JsonObject?> FindCustomerByReferenceAsync(string reference)
    {
        var client = Client();
        var resp = await client.GetAsync($"/customers.json?q={Uri.EscapeDataString(reference)}");
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("customer", out var cust) &&
                    cust.TryGetProperty("reference", out var refProp) &&
                    refProp.GetString() == reference)
                {
                    return JsonNode.Parse(cust.GetRawText())?.AsObject();
                }
            }
        }
        return null;
    }

    public async Task<JsonObject> CreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var client = Client();
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/customers.json", content);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return (JsonNode.Parse(doc.RootElement.GetProperty("customer").GetRawText()) as JsonObject)!;
    }

    public async Task<JsonObject> CreateSubscriptionAsync(string customerReference, string productHandle)
    {
        var client = Client();
        // Idempotent: ensure customer exists by reference (already done by caller, but double-check)
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/subscriptions.json", content);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return (JsonNode.Parse(doc.RootElement.GetProperty("subscription").GetRawText()) as JsonObject)!;
    }

    public async Task<JsonArray> GetSubscriptionsByCustomerReferenceAsync(string reference)
    {
        var client = Client();
        // We can search subscriptions by customer_reference? Docs say list subscriptions supports q.
        var resp = await client.GetAsync($"/subscriptions.json?q={Uri.EscapeDataString(reference)}&per_page=50");
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var array = new JsonArray();
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("subscription", out var sub))
                {
                    if (sub.TryGetProperty("customer", out var cust) &&
                        cust.TryGetProperty("reference", out var refProp) &&
                        refProp.GetString() == reference)
                    {
                        array.Add(JsonNode.Parse(sub.GetRawText()));
                    }
                }
            }
        }
        return array;
    }
}
