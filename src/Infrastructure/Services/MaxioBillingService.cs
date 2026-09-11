using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioBillingService(IOptions<MaxioSettings> settings, HttpClient httpClient)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        var apiKey = _settings.ApiKey ?? string.Empty;
        var baseUrl = GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X")));
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return _settings.BaseUrl;
        var sub = _settings.Subdomain ?? "cp-exp-4";
        return $"https://{sub}.chargify.com";
    }

    public async Task<string> GetCustomerReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var resp = await _httpClient.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("customer", out var c) && c.TryGetProperty("id", out var idProp))
            return idProp.GetInt32().ToString();
        return null;
    }

    public async Task<string> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
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
        var resp = await _httpClient.PostAsync("customers.json",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("customer", out var c) && c.TryGetProperty("id", out var idProp))
            return idProp.GetInt32().ToString();
        throw new InvalidOperationException("Failed to create customer");
    }

    public async Task<string> CreateSubscriptionAsync(string customerReference, string productHandle)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = customerReference
            }
        };
        var resp = await _httpClient.PostAsync("subscriptions.json",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Subscription create failed: {resp.StatusCode} - {err}");
        }
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("subscription", out var sub) && sub.TryGetProperty("id", out var idProp))
            return idProp.GetInt32().ToString();
        throw new InvalidOperationException("Failed to create subscription");
    }

    public async Task<List<SubscriptionInfo>> ListSubscriptionsAsync(string customerReference)
    {
        // No direct filter by customer reference; fetch all and filter by reference or look up via customer
        // For demo we'll query with per_page max and filter locally if needed.
        var resp = await _httpClient.GetAsync("subscriptions.json?per_page=200");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var result = new List<SubscriptionInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("subscription", out var sub))
                {
                    var id = sub.TryGetProperty("id", out var p) ? p.GetInt32() : 0;
                    var state = sub.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var handle = sub.TryGetProperty("product", out var prod) && prod.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "";
                    var nextBilling = sub.TryGetProperty("current_period_ends_at", out var n) ? n.GetString() : "";
                    var priceCents = sub.TryGetProperty("product_price_in_cents", out var priceProp) ? priceProp.GetInt64() : 0;
                    result.Add(new SubscriptionInfo { Id = id, State = state, ProductHandle = handle, NextBillingAt = nextBilling ?? "", PriceInCents = priceCents });
                }
            }
        }
        return result;
    }

    public async Task<List<ProductInfo>> ListProductsAsync()
    {
        var familyHandle = _settings.ProductFamilyHandle ?? "eshop-subscribe";
        var resp = await _httpClient.GetAsync("products.json?per_page=200");
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var result = new List<ProductInfo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var prod))
                {
                    string famHandle = "";
                    if (prod.TryGetProperty("product_family", out var fam) && fam.ValueKind != JsonValueKind.Null)
                    {
                        if (fam.TryGetProperty("handle", out var fh)) famHandle = fh.GetString() ?? "";
                    }
                    if (!string.Equals(famHandle, familyHandle, StringComparison.OrdinalIgnoreCase)) continue;
                    var id = prod.TryGetProperty("id", out var pid) ? pid.GetInt32() : 0;
                    var handle = prod.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "";
                    var name = prod.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var price = prod.TryGetProperty("price_in_cents", out var pc) ? pc.GetInt64() : 0;
                    result.Add(new ProductInfo { Id = id, Handle = handle, Name = name, PriceInCents = price, ProductFamilyHandle = famHandle });
                }
            }
        }
        return result;
    }
}
