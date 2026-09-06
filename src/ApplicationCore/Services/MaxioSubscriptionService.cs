using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioSubscriptionService
{
    Task<List<MaxioProduct>> ListProductsForFamilyAsync();
    Task<MaxioCustomer?> FindOrCreateCustomerAsync(string email, string firstName, string lastName, string userId);
    Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        HttpClient httpClient,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
    }

    public async Task<List<MaxioProduct>> ListProductsForFamilyAsync()
    {
        _logger.LogInformation("Listing products for family: {FamilyHandle}", _settings.ProductFamilyHandle);

        try
        {
            var url = $"{_settings.GetBaseUrl()}/products.json?product_family_handle={Uri.EscapeDataString(_settings.ProductFamilyHandle)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("API Error: {StatusCode}", response.StatusCode);
                return new List<MaxioProduct>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var listResponse = JsonSerializer.Deserialize<ListProductsResponse>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (listResponse?.Products != null)
            {
                return listResponse.Products;
            }

            _logger.LogWarning("No products found for family {FamilyHandle}", _settings.ProductFamilyHandle);
            return new List<MaxioProduct>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products for family {FamilyHandle}", _settings.ProductFamilyHandle);
            throw;
        }
    }

    public async Task<MaxioCustomer?> FindOrCreateCustomerAsync(string email, string firstName, string lastName, string userId)
    {
        _logger.LogInformation("Finding or creating customer for userId: {UserId}, email: {Email}", userId, email);

        try
        {
            var existing = await FindCustomerByReferenceAsync(userId);
            if (existing != null)
            {
                _logger.LogInformation("Found existing customer {CustomerId} for userId {UserId}", existing.Id, userId);
                return existing;
            }

            var newCustomer = await CreateCustomerAsync(email, firstName, lastName, userId);
            _logger.LogInformation("Created new customer {CustomerId} for userId {UserId}", newCustomer?.Id, userId);
            return newCustomer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding or creating customer for userId {UserId}", userId);
            throw;
        }
    }

    public async Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        _logger.LogInformation("Creating subscription for customer {CustomerId} with product {ProductHandle}",
            customerId, productHandle);

        try
        {
            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscriptionData
                {
                    CustomerId = customerId,
                    ProductHandle = productHandle
                }
            };

            var url = $"{_settings.GetBaseUrl()}/subscriptions.json";
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("API Error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CreateSubscriptionResponse>(responseContent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (result?.Subscription != null)
            {
                _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}",
                    result.Subscription.Id, customerId);
                return result.Subscription;
            }

            _logger.LogWarning("No subscription returned for customer {CustomerId}", customerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        _logger.LogInformation("Getting subscriptions for customer {CustomerId}", customerId);

        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/{customerId}/subscriptions.json";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("API Error: {StatusCode}", response.StatusCode);
                return new List<MaxioSubscription>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ListSubscriptionsResponse>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (result?.Subscriptions != null)
            {
                return result.Subscriptions;
            }

            _logger.LogWarning("No subscriptions found for customer {CustomerId}", customerId);
            return new List<MaxioSubscription>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            _logger.LogDebug("Looking up customer at {Url}", url);
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Customer not found with reference {Reference}", reference);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("API Error: {StatusCode} - {Content}", response.StatusCode, content);
                return null;
            }

            var content2 = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(content2))
            {
                _logger.LogWarning("Empty response from customer lookup");
                return null;
            }

            var result = JsonSerializer.Deserialize<FindCustomerResponse>(content2, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return result?.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error looking up customer with reference {Reference}", reference);
            return null;
        }
    }

    private async Task<MaxioCustomer?> CreateCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerData
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Reference = reference
            }
        };

        var url = $"{_settings.GetBaseUrl()}/customers.json";
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("API Error: {StatusCode} - {Content}", response.StatusCode, errorContent);
            return null;
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CreateCustomerResponse>(responseContent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return result?.Customer;
    }
}
