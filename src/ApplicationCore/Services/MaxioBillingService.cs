using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioBillingService(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _httpClient.BaseAddress = new Uri(_settings.GetBaseUrl());
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync()
    {
        var url = $"/product_families/{_settings.ProductFamilyHandle}/products.json";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            var products = root.GetProperty("products");

            var plans = new List<SubscriptionPlan>();
            foreach (var productElement in products.EnumerateArray())
            {
                var plan = JsonSerializer.Deserialize<SubscriptionPlan>(
                    productElement.GetRawText(),
                    _jsonOptions) ?? new SubscriptionPlan();

                plans.Add(plan);
            }

            return plans;
        }
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string externalId, string firstName, string lastName, string email)
    {
        var existingCustomer = await TryGetCustomerByReferenceAsync(externalId);
        if (existingCustomer != null)
            return existingCustomer;

        var createRequest = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = externalId
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(createRequest, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            var customerElement = root.GetProperty("customer");
            var customer = JsonSerializer.Deserialize<MaxioCustomer>(
                customerElement.GetRawText(),
                _jsonOptions) ?? throw new InvalidOperationException("Failed to deserialize customer");

            return customer;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle)
    {
        var createRequest = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(createRequest, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/subscriptions.json", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            var subscriptionElement = root.GetProperty("subscription");
            var subscription = JsonSerializer.Deserialize<MaxioSubscription>(
                subscriptionElement.GetRawText(),
                _jsonOptions) ?? throw new InvalidOperationException("Failed to deserialize subscription");

            return subscription;
        }
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId)
    {
        var url = $"/customers/{customerId}/subscriptions.json";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            var subscriptions = root.GetProperty("subscriptions");

            var list = new List<MaxioSubscription>();
            foreach (var subElement in subscriptions.EnumerateArray())
            {
                var sub = JsonSerializer.Deserialize<MaxioSubscription>(
                    subElement.GetRawText(),
                    _jsonOptions) ?? new MaxioSubscription();

                list.Add(sub);
            }

            return list;
        }
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(long subscriptionId)
    {
        var url = $"/subscriptions/{subscriptionId}.json";
        var response = await _httpClient.GetAsync(url);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            var subscriptionElement = root.GetProperty("subscription");
            var subscription = JsonSerializer.Deserialize<MaxioSubscription>(
                subscriptionElement.GetRawText(),
                _jsonOptions);

            return subscription;
        }
    }

    private async Task<MaxioCustomer?> TryGetCustomerByReferenceAsync(string reference)
    {
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _httpClient.GetAsync(url);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Failed to lookup customer: {response.StatusCode}");

        var json = await response.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            var customerElement = root.GetProperty("customer");
            var customer = JsonSerializer.Deserialize<MaxioCustomer>(
                customerElement.GetRawText(),
                _jsonOptions);

            return customer;
        }
    }
}
