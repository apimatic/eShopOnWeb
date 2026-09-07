using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioApiService
{
    Task<MaxioCustomerResponse?> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<MaxioProductResponse[]?> ListProductsAsync();
    Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle, string paymentCollectionMethod = "remittance");
    Task<MaxioSubscriptionResponse?> GetSubscriptionAsync(long subscriptionId);
    Task<MaxioSubscriptionResponse[]?> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioApiService : IMaxioApiService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioApiService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioApiService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<MaxioCustomerResponse?> GetOrCreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        try
        {
            // First, try to lookup the customer by reference
            var lookupUrl = $"{_settings.GetBaseUrl()}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var lookupRequest = new HttpRequestMessage(HttpMethod.Get, lookupUrl);
            AddAuthHeader(lookupRequest);

            var lookupResponse = await _httpClient.SendAsync(lookupRequest);

            if (lookupResponse.IsSuccessStatusCode)
            {
                var content = await lookupResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var customer = JsonSerializer.Deserialize<MaxioCustomerResponse>(
                    doc.RootElement.GetProperty("customer").GetRawText(),
                    _jsonOptions);
                _logger.LogInformation($"Found existing Maxio customer for reference {reference}");
                return customer;
            }

            // Customer doesn't exist, create one
            var createUrl = $"{_settings.GetBaseUrl()}/customers.json";
            var payload = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email,
                    reference
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var createRequest = new HttpRequestMessage(HttpMethod.Post, createUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            AddAuthHeader(createRequest);

            var createResponse = await _httpClient.SendAsync(createRequest);

            if (!createResponse.IsSuccessStatusCode)
            {
                var errorContent = await createResponse.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create Maxio customer: {createResponse.StatusCode} - {errorContent}");
                return null;
            }

            var responseContent = await createResponse.Content.ReadAsStringAsync();
            using var createDoc = JsonDocument.Parse(responseContent);
            var newCustomer = JsonSerializer.Deserialize<MaxioCustomerResponse>(
                createDoc.RootElement.GetProperty("customer").GetRawText(),
                _jsonOptions);
            _logger.LogInformation($"Created new Maxio customer {newCustomer?.Id} for reference {reference}");
            return newCustomer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOrCreateCustomerAsync");
            return null;
        }
    }

    public async Task<MaxioProductResponse[]?> ListProductsAsync()
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/products.json?family_id=handle:{Uri.EscapeDataString(_settings.ProductFamilyHandle)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to list Maxio products: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var productsArray = doc.RootElement;
            if (productsArray.ValueKind != JsonValueKind.Array)
                return null;

            var products = new List<MaxioProductResponse>();
            foreach (var item in productsArray.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var productElement))
                {
                    var product = JsonSerializer.Deserialize<MaxioProductResponse>(
                        productElement.GetRawText(),
                        _jsonOptions);
                    if (product != null)
                        products.Add(product);
                }
            }

            return products.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ListProductsAsync");
            return null;
        }
    }

    public async Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle, string paymentCollectionMethod = "remittance")
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/subscriptions.json";
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = paymentCollectionMethod
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create subscription: {response.StatusCode} - {errorContent}");
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var subscription = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(
                doc.RootElement.GetProperty("subscription").GetRawText(),
                _jsonOptions);
            _logger.LogInformation($"Created Maxio subscription {subscription?.Id} for customer {customerId}");
            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateSubscriptionAsync");
            return null;
        }
    }

    public async Task<MaxioSubscriptionResponse?> GetSubscriptionAsync(long subscriptionId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/subscriptions/{subscriptionId}.json";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get subscription: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var subscription = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(
                doc.RootElement.GetProperty("subscription").GetRawText(),
                _jsonOptions);
            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetSubscriptionAsync");
            return null;
        }
    }

    public async Task<MaxioSubscriptionResponse[]?> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"{_settings.GetBaseUrl()}/customers/{customerId}/subscriptions.json";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get customer subscriptions: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var subscriptions = JsonSerializer.Deserialize<MaxioSubscriptionResponse[]>(content, _jsonOptions);
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetCustomerSubscriptionsAsync");
            return null;
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }
}

public class MaxioCustomerResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MaxioProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
    public bool Taxable { get; set; }
}

public class MaxioSubscriptionResponse
{
    public long Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = null!;
    public long ProductPriceInCents { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public MaxioSubscriptionProduct? Product { get; set; }
    public MaxioSubscriptionCustomer? Customer { get; set; }
}

public class MaxioSubscriptionProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
}

public class MaxioSubscriptionCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Reference { get; set; }
}
