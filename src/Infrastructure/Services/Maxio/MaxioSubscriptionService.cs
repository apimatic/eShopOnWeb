using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public class MaxioSubscriptionService
{
    private readonly MaxioApiClient _apiClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioApiClient apiClient, MaxioSettings settings, ILogger<MaxioSubscriptionService> logger)
    {
        _apiClient = apiClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<MaxioProduct>> GetSubscriptionPlansAsync()
    {
        var endpoint = $"/product_families/handle:{_settings.ProductFamilyHandle}/products.json";
        var response = await _apiClient.GetAsync<MaxioProductsResponse>(endpoint);
        return response?.Items?.Select(item => item.Product).ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email)
    {
        var customerReference = $"eshop-{userId}";

        // Try to find existing customer by reference
        var listEndpoint = "/customers.json";
        var queryEndpoint = $"{listEndpoint}?reference={Uri.EscapeDataString(customerReference)}";

        try
        {
            var listResponse = await _apiClient.GetAsync<MaxioCustomersListResponse>(queryEndpoint);
            if (listResponse?.Customers?.Any() == true)
            {
                return listResponse.Customers.First();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list customers, attempting to create new one");
        }

        // Create new customer
        var createRequest = new MaxioCustomerRequest
        {
            Customer = new MaxioCustomerData
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = customerReference
            }
        };

        var createResponse = await _apiClient.PostAsync<MaxioCustomerResponse>("/customers.json", createRequest);
        return createResponse?.Customer;
    }

    public async Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var request = new MaxioSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionData
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        var response = await _apiClient.PostAsync<MaxioSubscriptionResponse>("/subscriptions.json", request);
        return response?.Subscription;
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        var endpoint = $"/customers/{customerId}/subscriptions.json";
        var response = await _apiClient.GetAsync<MaxioSubscriptionsListResponse>(endpoint);
        return response?.Subscriptions ?? new List<MaxioSubscription>();
    }

    public async Task<List<MaxioSubscription>> GetAllSubscriptionsAsync(int? customerId = null)
    {
        var endpoint = "/subscriptions.json";
        if (customerId.HasValue)
        {
            endpoint += $"?customer_id={customerId}";
        }

        var response = await _apiClient.GetAsync<MaxioSubscriptionsListResponse>(endpoint);
        return response?.Subscriptions ?? new List<MaxioSubscription>();
    }
}

public class MaxioCustomersListResponse
{
    public List<MaxioCustomer> Customers { get; set; } = new();
}
