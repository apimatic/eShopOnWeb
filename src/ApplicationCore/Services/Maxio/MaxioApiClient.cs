using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var baseUrl = _settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/products/lookup.json?handle={handle}");
            response.EnsureSuccessStatusCode();

            var product = await response.Content.ReadFromJsonAsync<MaxioProductResponse>();
            return product?.Product;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching product by handle: {handle}", handle);
            return null;
        }
    }

    public async Task<List<MaxioProduct>> ListProductsByFamilyHandleAsync(string familyHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/product_families/lookup.json?handle={familyHandle}/products.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioProductsListResponse>(content, options);

            return result?.Products ?? new List<MaxioProduct>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products by family handle: {familyHandle}", familyHandle);
            return new List<MaxioProduct>();
        }
    }

    public async Task<MaxioCustomer> CreateOrGetCustomerAsync(string userId, string email, string? firstName, string? lastName)
    {
        try
        {
            var createRequest = new CreateMaxioCustomerRequest
            {
                Customer = new MaxioCustomerData
                {
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    Reference = userId
                }
            };

            var json = JsonSerializer.Serialize(createRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/customers.json", content);

            if (response.IsSuccessStatusCode)
            {
                var customerResponse = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>();
                if (customerResponse?.Customer != null)
                {
                    return customerResponse.Customer;
                }
            }

            _logger.LogWarning("Failed to create/get customer for userId: {userId}", userId);
            throw new InvalidOperationException($"Failed to create/get customer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/getting customer for userId: {userId}", userId);
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var createRequest = new CreateMaxioSubscriptionRequest
            {
                Subscription = new CreateMaxioSubscriptionData
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle,
                    PaymentCollectionMethod = "remittance"
                }
            };

            var json = JsonSerializer.Serialize(createRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);

            response.EnsureSuccessStatusCode();

            var subscriptionResponse = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>();
            if (subscriptionResponse?.Subscription != null)
            {
                return subscriptionResponse.Subscription;
            }

            throw new InvalidOperationException("Failed to parse subscription response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customerId: {customerId}, productHandle: {productHandle}", customerId, productHandle);
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={customerId}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<MaxioSubscriptionsListResponse>(content, options);

            return result?.Subscriptions ?? new List<MaxioSubscription>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for customerId: {customerId}", customerId);
            return new List<MaxioSubscription>();
        }
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json");
            response.EnsureSuccessStatusCode();

            var subscriptionResponse = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>();
            return subscriptionResponse?.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription: {subscriptionId}", subscriptionId);
            return null;
        }
    }
}
