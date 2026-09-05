using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, string baseUrl, string apiKey, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _logger = logger;
        SetAuthHeader();
    }

    private void SetAuthHeader()
    {
        var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string productHandle)
    {
        try
        {
            var url = $"{_baseUrl}/products.json?handle={Uri.EscapeDataString(productHandle)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get product {Handle}: {StatusCode}", productHandle, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("products", out var productsArray) && productsArray.GetArrayLength() > 0)
            {
                var productElem = productsArray[0];
                return MapToMaxioProduct(productElem);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product {Handle}", productHandle);
            return null;
        }
    }

    public async Task<IEnumerable<MaxioProduct>> GetProductsByFamilyHandleAsync(string familyHandle)
    {
        try
        {
            var url = $"{_baseUrl}/products.json?product_family_handle={Uri.EscapeDataString(familyHandle)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get products for family {FamilyHandle}: {StatusCode}", familyHandle, response.StatusCode);
                return Enumerable.Empty<MaxioProduct>();
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var products = new List<MaxioProduct>();
            if (root.TryGetProperty("products", out var productsArray))
            {
                foreach (var productElem in productsArray.EnumerateArray())
                {
                    var product = MapToMaxioProduct(productElem);
                    if (product != null)
                        products.Add(product);
                }
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products for family {FamilyHandle}", familyHandle);
            return Enumerable.Empty<MaxioProduct>();
        }
    }

    public async Task<MaxioCustomerResponse> CreateOrGetCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        try
        {
            var existingCustomer = await GetCustomerByReferenceAsync(reference);
            if (existingCustomer != null)
            {
                return existingCustomer;
            }

            var url = $"{_baseUrl}/customers.json";
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

            var content = JsonContent.Create(payload);
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create customer: {StatusCode} - {Content}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to create customer: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerElem))
            {
                return MapToMaxioCustomerResponse(customerElem);
            }

            throw new InvalidOperationException("Unexpected response format from Maxio");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating or getting customer {Reference}", reference);
            throw;
        }
    }

    public async Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions.json";
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    payment_collection_method = "remittance"
                }
            };

            var content = JsonContent.Create(payload);
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create subscription: {StatusCode} - {Content}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscriptionElem))
            {
                return MapToMaxioSubscriptionResponse(subscriptionElem);
            }

            throw new InvalidOperationException("Unexpected response format from Maxio");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for customer {CustomerId} and product {ProductHandle}", customerId, productHandle);
            throw;
        }
    }

    public async Task<IEnumerable<MaxioSubscriptionResponse>> GetSubscriptionsByCustomerAsync(int customerId)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions.json?customer_id={customerId}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get subscriptions for customer {CustomerId}: {StatusCode}", customerId, response.StatusCode);
                return Enumerable.Empty<MaxioSubscriptionResponse>();
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var subscriptions = new List<MaxioSubscriptionResponse>();
            if (root.TryGetProperty("subscriptions", out var subscriptionsArray))
            {
                foreach (var subscriptionElem in subscriptionsArray.EnumerateArray())
                {
                    var subscription = MapToMaxioSubscriptionResponse(subscriptionElem);
                    subscriptions.Add(subscription);
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for customer {CustomerId}", customerId);
            return Enumerable.Empty<MaxioSubscriptionResponse>();
        }
    }

    private async Task<MaxioCustomerResponse?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerElem))
            {
                return MapToMaxioCustomerResponse(customerElem);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer by reference {Reference}", reference);
            return null;
        }
    }

    private static MaxioProduct? MapToMaxioProduct(JsonElement productElem)
    {
        return new MaxioProduct
        {
            Id = productElem.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : 0,
            Name = productElem.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? string.Empty : string.Empty,
            Handle = productElem.TryGetProperty("handle", out var handleElem) ? handleElem.GetString() ?? string.Empty : string.Empty,
            Description = productElem.TryGetProperty("description", out var descElem) ? descElem.GetString() ?? string.Empty : string.Empty,
            PriceInCents = productElem.TryGetProperty("price_in_cents", out var priceElem) ? priceElem.GetInt32() : 0,
            Interval = productElem.TryGetProperty("interval", out var intervalElem) ? intervalElem.GetInt32() : 1,
            IntervalUnit = productElem.TryGetProperty("interval_unit", out var unitElem) ? unitElem.GetString() ?? "month" : "month"
        };
    }

    private static MaxioCustomerResponse MapToMaxioCustomerResponse(JsonElement customerElem)
    {
        return new MaxioCustomerResponse
        {
            Id = customerElem.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : 0,
            FirstName = customerElem.TryGetProperty("first_name", out var fnElem) ? fnElem.GetString() ?? string.Empty : string.Empty,
            LastName = customerElem.TryGetProperty("last_name", out var lnElem) ? lnElem.GetString() ?? string.Empty : string.Empty,
            Email = customerElem.TryGetProperty("email", out var emailElem) ? emailElem.GetString() ?? string.Empty : string.Empty,
            Reference = customerElem.TryGetProperty("reference", out var refElem) && refElem.ValueKind != JsonValueKind.Null ? refElem.GetString() : null,
            CreatedAt = customerElem.TryGetProperty("created_at", out var createdElem) && DateTime.TryParse(createdElem.GetString(), out var createdAt) ? createdAt : DateTime.UtcNow,
            UpdatedAt = customerElem.TryGetProperty("updated_at", out var updatedElem) && DateTime.TryParse(updatedElem.GetString(), out var updatedAt) ? updatedAt : DateTime.UtcNow
        };
    }

    private static MaxioSubscriptionResponse MapToMaxioSubscriptionResponse(JsonElement subscriptionElem)
    {
        var result = new MaxioSubscriptionResponse
        {
            Id = subscriptionElem.TryGetProperty("id", out var idElem) ? idElem.GetInt32() : 0,
            State = subscriptionElem.TryGetProperty("state", out var stateElem) ? stateElem.GetString() ?? string.Empty : string.Empty,
            ProductPriceInCents = subscriptionElem.TryGetProperty("product_price_in_cents", out var priceElem) ? priceElem.GetInt32() : 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (subscriptionElem.TryGetProperty("product", out var productElem) && productElem.ValueKind == JsonValueKind.Object)
        {
            result.ProductHandle = productElem.TryGetProperty("handle", out var handleElem) ? handleElem.GetString() ?? string.Empty : string.Empty;
        }

        if (subscriptionElem.TryGetProperty("current_period_ends_at", out var periodElem) && DateTime.TryParse(periodElem.GetString(), out var periodDate))
        {
            result.CurrentPeriodEndsAt = periodDate;
        }

        if (subscriptionElem.TryGetProperty("next_assessment_at", out var nextElem) && DateTime.TryParse(nextElem.GetString(), out var nextDate))
        {
            result.NextAssessmentAt = nextDate;
        }

        if (subscriptionElem.TryGetProperty("activated_at", out var activatedElem) && DateTime.TryParse(activatedElem.GetString(), out var activatedDate))
        {
            result.ActivatedAt = activatedDate;
        }

        if (subscriptionElem.TryGetProperty("created_at", out var createdElem) && DateTime.TryParse(createdElem.GetString(), out var createdDate))
        {
            result.CreatedAt = createdDate;
        }

        if (subscriptionElem.TryGetProperty("updated_at", out var updatedElem) && DateTime.TryParse(updatedElem.GetString(), out var updatedDate))
        {
            result.UpdatedAt = updatedDate;
        }

        return result;
    }
}
