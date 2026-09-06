using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

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
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var baseUrl = _settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", GetAuthHeader());
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    private string GetAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        return $"Basic {credentials}";
    }

    public async Task<MaxioCustomerResponse?> GetOrCreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        try
        {
            var customer = await GetCustomerByReferenceAsync(reference);
            if (customer != null)
                return customer;

            return await CreateCustomerAsync(reference, firstName, lastName, email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer with reference {Reference}", reference);
            throw;
        }
    }

    private async Task<MaxioCustomerResponse?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get customer by reference. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, content);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var customerObj = doc.RootElement.GetProperty("customer");
            return ParseCustomer(customerObj);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer by reference");
            throw;
        }
    }

    private async Task<MaxioCustomerResponse> CreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        try
        {
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
            var response = await _httpClient.PostAsync("/customers.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create customer. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, errorContent);
                throw new Exception($"Failed to create customer: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var customerObj = doc.RootElement.GetProperty("customer");
            return ParseCustomer(customerObj);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer");
            throw;
        }
    }

    public async Task<List<MaxioProductResponse>> ListProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/products.json");

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to list products. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, content);
                throw new Exception($"Failed to list products: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var products = new List<MaxioProductResponse>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var productWrapperObj in doc.RootElement.EnumerateArray())
                {
                    if (productWrapperObj.TryGetProperty("product", out var productObj))
                    {
                        var product = ParseProduct(productObj);
                        // Filter by product family handle if specified
                        if (string.IsNullOrEmpty(_settings.ProductFamilyHandle) ||
                            productObj.TryGetProperty("product_family", out var familyObj) &&
                            familyObj.TryGetProperty("handle", out var handleProp) &&
                            handleProp.GetString() == _settings.ProductFamilyHandle)
                        {
                            products.Add(product);
                        }
                    }
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

    public async Task<MaxioSubscriptionResponse> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create subscription. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, errorContent);
                throw new Exception($"Failed to create subscription: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var subscriptionObj = doc.RootElement.GetProperty("subscription");
            return ParseSubscription(subscriptionObj);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            throw;
        }
    }

    public async Task<MaxioSubscriptionResponse?> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json");

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get subscription. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, content);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var subscriptionObj = doc.RootElement.GetProperty("subscription");
            return ParseSubscription(subscriptionObj);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription");
            throw;
        }
    }

    public async Task<List<MaxioSubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to list customer subscriptions. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, content);
                throw new Exception($"Failed to list subscriptions: {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var subscriptions = new List<MaxioSubscriptionResponse>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var subscriptionObj in doc.RootElement.EnumerateArray())
                {
                    var subObj = subscriptionObj.GetProperty("subscription");
                    subscriptions.Add(ParseSubscription(subObj));
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing customer subscriptions");
            throw;
        }
    }

    private MaxioCustomerResponse ParseCustomer(JsonElement customerObj)
    {
        return new MaxioCustomerResponse
        {
            Id = customerObj.GetProperty("id").GetInt32(),
            FirstName = customerObj.GetProperty("first_name").GetString() ?? "",
            LastName = customerObj.GetProperty("last_name").GetString() ?? "",
            Email = customerObj.GetProperty("email").GetString() ?? "",
            Reference = customerObj.TryGetProperty("reference", out var refProp) && refProp.ValueKind != JsonValueKind.Null
                ? refProp.GetString()
                : null
        };
    }

    private MaxioProductResponse ParseProduct(JsonElement productObj)
    {
        return new MaxioProductResponse
        {
            Id = productObj.GetProperty("id").GetInt32(),
            Name = productObj.GetProperty("name").GetString() ?? "",
            Handle = productObj.GetProperty("handle").GetString() ?? "",
            Description = productObj.TryGetProperty("description", out var descProp) && descProp.ValueKind != JsonValueKind.Null
                ? descProp.GetString()
                : null,
            PriceInCents = productObj.GetProperty("price_in_cents").GetInt32(),
            Interval = productObj.GetProperty("interval").GetInt32(),
            IntervalUnit = productObj.GetProperty("interval_unit").GetString() ?? ""
        };
    }

    private MaxioSubscriptionResponse ParseSubscription(JsonElement subscriptionObj)
    {
        var subscription = new MaxioSubscriptionResponse
        {
            Id = subscriptionObj.GetProperty("id").GetInt32(),
            CustomerId = subscriptionObj.GetProperty("customer_id").GetInt32(),
            State = subscriptionObj.GetProperty("state").GetString() ?? "",
            CreatedAt = ParseDateTime(subscriptionObj, "created_at"),
            UpdatedAt = ParseDateTime(subscriptionObj, "updated_at"),
            NextAssessmentAt = ParseDateTime(subscriptionObj, "next_assessment_at")
        };

        if (subscriptionObj.TryGetProperty("product", out var productProp) && productProp.ValueKind != JsonValueKind.Null)
        {
            subscription.Product = ParseProduct(productProp);
        }

        return subscription;
    }

    private DateTime? ParseDateTime(JsonElement obj, string propertyName)
    {
        if (obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            if (prop.TryGetDateTime(out var dt))
                return dt;
        }
        return null;
    }
}
