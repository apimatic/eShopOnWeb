using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly string _authHeaderValue;

    public MaxioApiClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _authHeaderValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _authHeaderValue);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        try
        {
            var existing = await GetCustomerByReferenceAsync(reference);
            if (existing != null)
            {
                return existing;
            }
        }
        catch
        {
            // Customer not found, will create new one
        }

        return await CreateCustomerAsync(reference, firstName, lastName, email);
    }

    private async Task<MaxioCustomer?> CreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var requestBody = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference,
                country = "US"
            }
        };

        var json = JsonSerializer.Serialize(requestBody, GetJsonSerializerOptions());
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_settings.GetBaseUrl()}/customers.json", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = GetJsonSerializerOptions();
        var result = JsonSerializer.Deserialize<CustomerResponse>(responseContent, options);
        return result?.Customer;
    }

    public async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference)
    {
        var response = await _httpClient.GetAsync($"{_settings.GetBaseUrl()}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = GetJsonSerializerOptions();
        var result = JsonSerializer.Deserialize<CustomerResponse>(responseContent, options);
        return result?.Customer;
    }

    public async Task<List<MaxioProduct>> ListProductsAsync()
    {
        var response = await _httpClient.GetAsync($"{_settings.GetBaseUrl()}/products.json");
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = GetJsonSerializerOptions();
        var result = JsonSerializer.Deserialize<List<ProductResponse>>(responseContent, options);
        return result?.Select(r => r.Product).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioSubscription?> CreateSubscriptionAsync(long customerId, string productHandle)
    {
        var requestBody = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle
            }
        };

        var json = JsonSerializer.Serialize(requestBody, GetJsonSerializerOptions());
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_settings.GetBaseUrl()}/subscriptions.json", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = GetJsonSerializerOptions();
        var result = JsonSerializer.Deserialize<SubscriptionResponse>(responseContent, options);
        return result?.Subscription;
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId)
    {
        var response = await _httpClient.GetAsync($"{_settings.GetBaseUrl()}/subscriptions.json?customer_id={customerId}");
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = GetJsonSerializerOptions();
        var result = JsonSerializer.Deserialize<List<SubscriptionResponse>>(responseContent, options);
        return result?.Select(r => r.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    private static JsonSerializerOptions GetJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
    }

    private class CustomerResponse
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class ProductResponse
    {
        [JsonPropertyName("product")]
        public MaxioProduct Product { get; set; } = new();
    }

    private class SubscriptionResponse
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription Subscription { get; set; } = new();
    }
}
