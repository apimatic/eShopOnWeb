using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<JsonObject?> GetProductFamilyAsync();
    Task<JsonArray?> GetProductsAsync();
    Task<JsonObject?> GetCustomerByReferenceAsync(string reference);
    Task<JsonObject?> CreateCustomerAsync(string reference, string email, string firstName = "", string lastName = "");
    Task<JsonObject?> CreateSubscriptionAsync(int customerId, int productId, string reference = "");
    Task<JsonArray?> GetSubscriptionsByCustomerAsync(int customerId);
    Task<JsonArray?> GetSubscriptionsByReferenceAsync(string reference);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient client, MaxioSettings settings, ILogger<MaxioService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
        _client.BaseAddress = new Uri(GetBaseUrl());
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return _settings.BaseUrl.TrimEnd('/') + "/";
        return $"https://{_settings.Subdomain}.chargify.com/api/v2/";
    }

    public async Task<JsonObject?> GetProductFamilyAsync()
    {
        try
        {
            var resp = await _client.GetAsync($"product_families/handle/{_settings.ProductFamilyHandle}.json");
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(text);
            return JsonNode.Parse(text)?.AsObject();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Maxio get family failed"); return null; }
    }

    public async Task<JsonArray?> GetProductsAsync()
    {
        try
        {
            // Products for family via family products endpoint or direct
            var resp = await _client.GetAsync($"products.json");
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(text);
            if (node is JsonObject obj && obj["products"] is JsonArray arr) return arr;
            return node?.AsArray();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Maxio get products failed"); return null; }
    }

    public async Task<JsonObject?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var resp = await _client.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(text)?.AsObject();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Maxio lookup customer failed"); return null; }
    }

    public async Task<JsonObject?> CreateCustomerAsync(string reference, string email, string firstName = "", string lastName = "")
    {
        try
        {
            var payload = new
            {
                customer = new
                {
                    reference,
                    email,
                    first_name = firstName,
                    last_name = lastName
                }
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _client.PostAsync("customers.json", content);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogError("Maxio create customer error {Status}: {Body}", resp.StatusCode, err);
                return null;
            }
            var text = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(text)?.AsObject();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Maxio create customer failed"); return null; }
    }

    public async Task<JsonObject?> CreateSubscriptionAsync(int customerId, int productId, string reference = "")
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    product_id = productId,
                    customer_id = customerId,
                    reference = reference,
                    coupon_code = (string?)null,
                    country = (string?)null
                }
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _client.PostAsync("subscriptions.json", content);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync();
                _logger.LogError("Maxio create subscription error {Status}: {Body}", resp.StatusCode, err);
                return null;
            }
            var text = await resp.Content.ReadAsStringAsync();
            return JsonNode.Parse(text)?.AsObject();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Maxio create subscription failed"); return null; }
    }

    public async Task<JsonArray?> GetSubscriptionsByCustomerAsync(int customerId)
    {
        try
        {
            var resp = await _client.GetAsync($"subscriptions.json?customer_id={customerId}");
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(text);
            if (node is JsonObject obj && obj["subscriptions"] is JsonArray arr) return arr;
            return node?.AsArray();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Maxio list subs failed"); return null; }
    }

    public async Task<JsonArray?> GetSubscriptionsByReferenceAsync(string reference)
    {
        try
        {
            var resp = await _client.GetAsync($"subscriptions.json?reference={Uri.EscapeDataString(reference)}");
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(text);
            if (node is JsonObject obj && obj["subscriptions"] is JsonArray arr) return arr;
            return node?.AsArray();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Maxio list subs by ref failed"); return null; }
    }
}
