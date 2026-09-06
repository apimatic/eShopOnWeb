using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioService
{
    MaxioConfiguration GetConfiguration();
    Task<MaxioProductsResponse?> ListProductsAsync(string productFamilyHandle);
    Task<MaxioCustomerResponse?> CreateOrGetCustomerAsync(string email, string firstName, string lastName, string reference);
    Task<MaxioCustomerResponse?> GetCustomerByReferenceAsync(string reference);
    Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<MaxioSubscriptionsListResponse?> ListSubscriptionsByCustomerAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, IOptions<MaxioConfiguration> options, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _config = options.Value;
        _logger = logger;
    }

    public MaxioConfiguration GetConfiguration() => _config;

    public async Task<MaxioProductsResponse?> ListProductsAsync(string productFamilyHandle)
    {
        try
        {
            var baseUrl = _config.GetBaseUrl();
            var url = $"{baseUrl}/products.json?product_family_handle={productFamilyHandle}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Maxio ListProducts failed: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<MaxioProductsResponse>(content, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio ListProducts");
            return null;
        }
    }

    public async Task<MaxioCustomerResponse?> CreateOrGetCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        try
        {
            var existing = await GetCustomerByReferenceAsync(reference);
            if (existing != null)
            {
                return existing;
            }

            return await CreateCustomerAsync(email, firstName, lastName, reference);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateOrGetCustomerAsync");
            return null;
        }
    }

    public async Task<MaxioCustomerResponse?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var baseUrl = _config.GetBaseUrl();
            var url = $"{baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<MaxioCustomerResponse>(content, options);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Customer lookup not found");
            return null;
        }
    }

    private async Task<MaxioCustomerResponse?> CreateCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        try
        {
            var baseUrl = _config.GetBaseUrl();
            var url = $"{baseUrl}/customers.json";

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

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Maxio CreateCustomer failed: {response.StatusCode} - {errorContent}");
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<MaxioCustomerResponse>(responseContent, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio CreateCustomer");
            return null;
        }
    }

    public async Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var baseUrl = _config.GetBaseUrl();
            var url = $"{baseUrl}/subscriptions.json";

            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Maxio CreateSubscription failed: {response.StatusCode} - {errorContent}");
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseContent, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio CreateSubscription");
            return null;
        }
    }

    public async Task<MaxioSubscriptionsListResponse?> ListSubscriptionsByCustomerAsync(int customerId)
    {
        try
        {
            var baseUrl = _config.GetBaseUrl();
            var url = $"{baseUrl}/subscriptions.json?customer_id={customerId}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Maxio ListSubscriptions failed: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<MaxioSubscriptionsListResponse>(content, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio ListSubscriptions");
            return null;
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        request.Headers.Add("Authorization", $"Basic {credentials}");
    }
}

#region Maxio Response DTOs

public class MaxioProductsResponse
{
    public List<MaxioProduct> Products { get; set; } = new();
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public string? Description { get; set; }
}

public class MaxioCustomerResponse
{
    public MaxioCustomer Customer { get; set; } = new();
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioSubscriptionResponse
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public MaxioProduct? Product { get; set; }
}

public class MaxioSubscriptionsListResponse
{
    public List<MaxioSubscriptionItem> Subscriptions { get; set; } = new();
}

public class MaxioSubscriptionItem
{
    public MaxioSubscription Subscription { get; set; } = new();
}

#endregion
