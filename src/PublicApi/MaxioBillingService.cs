using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioBillingService
{
    Task<JsonNode?> GetSubscriptionPlansAsync();
    Task<JsonNode?> CreateCustomerAsync(string email, string reference);
    Task<JsonNode?> GetCustomerByReferenceAsync(string reference);
    Task<JsonNode?> CreateSubscriptionAsync(string customerReference, int productFamilyId, int productId, string? email = null);
    Task<JsonNode?> GetSubscriptionsByCustomerReferenceAsync(string reference);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioBillingService(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
        var baseUrl = !string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? _settings.BaseUrl!.TrimEnd('/')
            : $"https://{_settings.Subdomain}.chargify.com";
        _http.BaseAddress = new Uri(baseUrl + "/");
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonNode?> GetSubscriptionPlansAsync()
    {
        // Use product family handle to locate family; for now return family details
        // Standard Chargify endpoint for product families
        var url = $"api/v1/product_families.json?handle={_settings.ProductFamilyHandle}";
        try
        {
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(text);
        }
        catch { return null; }
    }

    public async Task<JsonNode?> CreateCustomerAsync(string email, string reference)
    {
        var url = "api/v1/customers.json";
        var payload = new { customer = new { email, reference } };
        try
        {
            var resp = await _http.PostAsync(url, new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(text);
        }
        catch { return null; }
    }

    public async Task<JsonNode?> GetCustomerByReferenceAsync(string reference)
    {
        var url = $"api/v1/customers/lookup.json?reference={reference}";
        try
        {
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(text);
        }
        catch { return null; }
    }

    public async Task<JsonNode?> CreateSubscriptionAsync(string customerReference, int productFamilyId, int productId, string? email = null)
    {
        var url = "api/v1/subscriptions.json";
        var payload = new
        {
            subscription = new
            {
                customer_reference = customerReference,
                product_family_id = productFamilyId,
                product_id = productId,
                product_handle = "eshop-pro",
                email
            }
        };
        try
        {
            var resp = await _http.PostAsync(url, new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(text);
        }
        catch { return null; }
    }

    public async Task<JsonNode?> GetSubscriptionsByCustomerReferenceAsync(string reference)
    {
        var url = $"api/v1/subscriptions.json?customer_reference={reference}";
        try
        {
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(text);
        }
        catch { return null; }
    }
}
