using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<JsonNode?> GetPlansAsync();
    Task<JsonNode?> FindCustomerByReferenceAsync(string reference);
    Task<JsonNode?> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<JsonNode?> CreateSubscriptionAsync(string customerReference, string productHandle);
    Task<JsonNode?> GetCustomerSubscriptionsAsync(string customerReference);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioService(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        var baseUrl = !string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? _settings.BaseUrl.TrimEnd('/')
            : $"https://{_settings.Subdomain}.chargify.com";
        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
    }

    public async Task<JsonNode?> GetPlansAsync()
    {
        var resp = await _httpClient.GetAsync("products.json");
        if (!resp.IsSuccessStatusCode) return null;
        var text = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonNode>(text);
        if (doc == null) return null;
        // Filter by product family handle if response has products array
        if (doc["products"] is System.Text.Json.Nodes.JsonArray arr)
        {
            var filtered = new System.Text.Json.Nodes.JsonArray();
            foreach (var item in arr)
            {
                if (item?["product_family"]?["handle"]?.GetValue<string>() == _settings.ProductFamilyHandle)
                {
                    filtered.Add(item);
                }
            }
            return new System.Text.Json.Nodes.JsonObject { ["products"] = filtered };
        }
        return doc;
    }

    public async Task<JsonNode?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var resp = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (resp.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var text = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<JsonNode>(text);
            }
        }
        catch { }
        return null;
    }

    public async Task<JsonNode?> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference,
                organization = "",
                address = "",
                address_2 = "",
                city = "",
                state = "",
                zip = "",
                country = "US",
                phone = ""
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _httpClient.PostAsync("customers.json", content);
        if (resp.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<JsonNode>(await resp.Content.ReadAsStringAsync());
        }
        return null;
    }

    public async Task<JsonNode?> CreateSubscriptionAsync(string customerReference, string productHandle)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                payment_collection_method = "automatic"
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _httpClient.PostAsync("subscriptions.json", content);
        if (resp.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<JsonNode>(await resp.Content.ReadAsStringAsync());
        }
        return null;
    }

    public async Task<JsonNode?> GetCustomerSubscriptionsAsync(string customerReference)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference);
        if (customer == null) return null;
        var id = customer["customer"]?["id"]?.GetValue<int>() ?? 0;
        if (id == 0) return null;
        var resp = await _httpClient.GetAsync($"customers/{id}/subscriptions.json");
        if (resp.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<JsonNode>(await resp.Content.ReadAsStringAsync());
        }
        return null;
    }
}
