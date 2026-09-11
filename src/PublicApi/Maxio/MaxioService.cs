using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<CustomerResponse?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
            var customerObj = doc?["customer"];
            if (customerObj == null) return null;
            return new CustomerResponse
            {
                id = customerObj["id"]?.GetValue<int>() ?? 0,
                reference = customerObj["reference"]?.GetValue<string>() ?? reference,
                email = customerObj["email"]?.GetValue<string>() ?? string.Empty,
                first_name = customerObj["first_name"]?.GetValue<string>() ?? string.Empty,
                last_name = customerObj["last_name"]?.GetValue<string>() ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find customer by reference {Reference}", reference);
            return null;
        }
    }

    public async Task<CustomerResponse> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
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
        var response = await _httpClient.PostAsJsonAsync("/customers.json", payload);
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
        var c = doc?["customer"];
        return new CustomerResponse
        {
            id = c?["id"]?.GetValue<int>() ?? 0,
            reference = c?["reference"]?.GetValue<string>() ?? reference,
            email = c?["email"]?.GetValue<string>() ?? email,
            first_name = c?["first_name"]?.GetValue<string>() ?? firstName,
            last_name = c?["last_name"]?.GetValue<string>() ?? lastName
        };
    }

    public async Task<List<ProductResponse>> ListPlansAsync(string familyHandle)
    {
        var response = await _httpClient.GetAsync($"/product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json");
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
        var items = doc?["items"]?.AsArray();
        var products = new List<ProductResponse>();
        if (items != null)
        {
            foreach (var item in items)
            {
                var prod = item?["product"];
                if (prod != null)
                {
                    products.Add(new ProductResponse
                    {
                        id = prod["id"]?.GetValue<int>() ?? 0,
                        name = prod["name"]?.GetValue<string>() ?? string.Empty,
                        handle = prod["handle"]?.GetValue<string>() ?? string.Empty,
                        price_in_cents = prod["price_in_cents"]?.GetValue<long>() ?? 0,
                        interval = prod["interval"]?.GetValue<int>() ?? 0,
                        interval_unit = prod["interval_unit"]?.GetValue<string>() ?? string.Empty
                    });
                }
            }
        }
        return products;
    }

    public async Task<SubscriptionResponse> CreateSubscriptionAsync(string productHandle, string customerReference)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference
            }
        };
        var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", payload);
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
        var sub = doc?["subscription"];
        return new SubscriptionResponse
        {
            id = sub?["id"]?.GetValue<int>() ?? 0,
            state = sub?["state"]?.GetValue<string>() ?? string.Empty,
            product_price_in_cents = sub?["product_price_in_cents"]?.GetValue<long>() ?? 0,
            current_period_ends_at = sub?["current_period_ends_at"]?.GetValue<string>() ?? string.Empty,
            customer_id = sub?["customer_id"]?.GetValue<int>() ?? 0,
            product = sub?["product"] != null ? new ProductResponse
            {
                id = sub["product"]["id"]?.GetValue<int>() ?? 0,
                name = sub["product"]["name"]?.GetValue<string>() ?? string.Empty,
                handle = sub["product"]["handle"]?.GetValue<string>() ?? string.Empty,
                price_in_cents = sub["product"]["price_in_cents"]?.GetValue<long>() ?? 0,
                interval = sub["product"]["interval"]?.GetValue<int>() ?? 0,
                interval_unit = sub["product"]["interval_unit"]?.GetValue<string>() ?? string.Empty
            } : null
        };
    }

    public async Task<List<SubscriptionResponse>> ListSubscriptionsForCustomerAsync(int customerId)
    {
        var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
        var items = doc?["items"]?.AsArray();
        var subs = new List<SubscriptionResponse>();
        if (items != null)
        {
            foreach (var item in items)
            {
                var sub = item?["subscription"];
                if (sub != null)
                {
                    subs.Add(new SubscriptionResponse
                    {
                        id = sub["id"]?.GetValue<int>() ?? 0,
                        state = sub["state"]?.GetValue<string>() ?? string.Empty,
                        product_price_in_cents = sub["product_price_in_cents"]?.GetValue<long>() ?? 0,
                        current_period_ends_at = sub["current_period_ends_at"]?.GetValue<string>() ?? string.Empty,
                        customer_id = sub["customer_id"]?.GetValue<int>() ?? 0,
                        product = sub["product"] != null ? new ProductResponse
                        {
                            id = sub["product"]["id"]?.GetValue<int>() ?? 0,
                            name = sub["product"]["name"]?.GetValue<string>() ?? string.Empty,
                            handle = sub["product"]["handle"]?.GetValue<string>() ?? string.Empty,
                            price_in_cents = sub["product"]["price_in_cents"]?.GetValue<long>() ?? 0,
                            interval = sub["product"]["interval"]?.GetValue<int>() ?? 0,
                            interval_unit = sub["product"]["interval_unit"]?.GetValue<string>() ?? string.Empty
                        } : null
                    });
                }
            }
        }
        return subs;
    }
}
