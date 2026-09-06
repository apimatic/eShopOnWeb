using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioApiClient
{
    Task<MaxioCustomerResponse?> CreateOrGetCustomerAsync(string reference, string firstName, string lastName, string email);
    Task<List<MaxioProductResponse>> ListProductsAsync(string? familyHandle = null);
    Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;
    private string _baseUrl = string.Empty;

    public MaxioApiClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _baseUrl = _settings.BaseUrl ?? GetDefaultBaseUrl();
    }

    private string GetDefaultBaseUrl()
    {
        return $"https://{_settings.Subdomain}.chargify.com";
    }

    private void SetupAuth()
    {
        var authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomerResponse?> CreateOrGetCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        SetupAuth();

        try
        {
            // Try to get existing customer first
            var existingUrl = $"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(existingUrl);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("customer", out var customerElement))
                {
                    var customerId = customerElement.GetProperty("id").GetInt32();
                    _logger.LogInformation($"Found existing Maxio customer: {customerId}");
                    return new MaxioCustomerResponse { Id = customerId };
                }
            }

            // Customer doesn't exist, create it
            var createUrl = $"{_baseUrl}/customers.json";
            var createRequest = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = reference
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(createRequest),
                Encoding.UTF8,
                "application/json"
            );

            var createResponse = await _httpClient.PostAsync(createUrl, jsonContent);
            if (createResponse.IsSuccessStatusCode)
            {
                var content = await createResponse.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(content);
                var customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();
                _logger.LogInformation($"Created new Maxio customer: {customerId}");
                return new MaxioCustomerResponse { Id = customerId };
            }

            _logger.LogError($"Failed to create customer: {createResponse.StatusCode} - {await createResponse.Content.ReadAsStringAsync()}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/getting customer");
            throw;
        }
    }

    public async Task<List<MaxioProductResponse>> ListProductsAsync(string? familyHandle = null)
    {
        SetupAuth();

        try
        {
            var url = $"{_baseUrl}/products.json";
            if (!string.IsNullOrEmpty(familyHandle))
            {
                url += $"?family_handle={Uri.EscapeDataString(familyHandle)}";
            }

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to list products: {response.StatusCode}");
                return [];
            }

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            var products = new List<MaxioProductResponse>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("product", out var productElement))
                {
                    var product = new MaxioProductResponse
                    {
                        Id = productElement.GetProperty("id").GetInt32(),
                        Name = productElement.GetProperty("name").GetString() ?? string.Empty,
                        Handle = productElement.GetProperty("handle").GetString() ?? string.Empty,
                        PriceInCents = productElement.GetProperty("price_in_cents").GetInt32(),
                        IntervalUnit = productElement.GetProperty("interval_unit").GetString() ?? string.Empty
                    };
                    products.Add(product);
                }
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products");
            throw;
        }
    }

    public async Task<MaxioSubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        SetupAuth();

        try
        {
            var url = $"{_baseUrl}/subscriptions.json";
            var createRequest = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "remittance"
                }
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(createRequest),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, jsonContent);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(content);
                var subscriptionElement = doc.RootElement.GetProperty("subscription");

                var subscription = new MaxioSubscriptionResponse
                {
                    Id = subscriptionElement.GetProperty("id").GetInt32(),
                    State = subscriptionElement.GetProperty("state").GetString() ?? string.Empty,
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    CurrentPeriodEndsAt = subscriptionElement.GetProperty("current_period_ends_at").GetString(),
                    NextAssessmentAt = subscriptionElement.GetProperty("next_assessment_at").GetString()
                };
                _logger.LogInformation($"Created subscription {subscription.Id}");
                return subscription;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Failed to create subscription: {response.StatusCode} - {errorContent}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            throw;
        }
    }

    public async Task<List<MaxioSubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId)
    {
        SetupAuth();

        try
        {
            var url = $"{_baseUrl}/subscriptions.json?customer_id={customerId}&state=active";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to list subscriptions: {response.StatusCode}");
                return [];
            }

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);
            var subscriptions = new List<MaxioSubscriptionResponse>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("subscription", out var subElement))
                {
                    var subscription = new MaxioSubscriptionResponse
                    {
                        Id = subElement.GetProperty("id").GetInt32(),
                        State = subElement.GetProperty("state").GetString() ?? string.Empty,
                        ProductHandle = subElement.TryGetProperty("product", out var prodElem) &&
                                       prodElem.TryGetProperty("handle", out var handleElem)
                            ? handleElem.GetString() ?? string.Empty
                            : string.Empty,
                        CustomerId = customerId,
                        CurrentPeriodEndsAt = subElement.GetProperty("current_period_ends_at").GetString(),
                        NextAssessmentAt = subElement.GetProperty("next_assessment_at").GetString()
                    };
                    subscriptions.Add(subscription);
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions");
            throw;
        }
    }
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

public class MaxioProductResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }
}
