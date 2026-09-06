using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public interface IMaxioApiClient
{
    Task<MaxioApiCustomer?> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string reference);
    Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, int productId);
    Task<List<MaxioProduct>> GetProductsAsync();
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> options, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.BaseAddress = new Uri(_settings.GetApiBaseUrl());
    }

    public async Task<MaxioApiCustomer?> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        try
        {
            // Try to find existing customer by reference
            var listResponse = await _httpClient.GetAsync("/customers.json");
            if (listResponse.IsSuccessStatusCode)
            {
                var content = await listResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var customer in root.EnumerateArray())
                    {
                        if (customer.TryGetProperty("customer", out var custObj))
                        {
                            if (custObj.TryGetProperty("reference", out var refProp) &&
                                refProp.GetString() == reference)
                            {
                                return ExtractCustomer(custObj);
                            }
                        }
                    }
                }
            }

            // Create new customer
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

            var json = JsonSerializer.Serialize(createRequest);
            var content_create = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/customers.json", content_create);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                if (root.TryGetProperty("customer", out var customer))
                {
                    return ExtractCustomer(customer);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create Maxio customer: {response.StatusCode} - {errorContent}");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOrCreateCustomerAsync");
            throw;
        }
    }

    public async Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, int productId)
    {
        try
        {
            var createRequest = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_id = productId,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(createRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                if (root.TryGetProperty("subscription", out var subscription))
                {
                    return ExtractSubscription(subscription);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create Maxio subscription: {response.StatusCode} - {errorContent}");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateSubscriptionAsync");
            throw;
        }
    }

    public async Task<List<MaxioProduct>> GetProductsAsync()
    {
        var products = new List<MaxioProduct>();
        try
        {
            var response = await _httpClient.GetAsync("/products.json");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.TryGetProperty("product", out var product))
                        {
                            var productFamily = new MaxioProductFamily();
                            if (product.TryGetProperty("product_family", out var family))
                            {
                                productFamily = ExtractProductFamily(family);
                            }

                            var prod = new MaxioProduct
                            {
                                Id = product.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                                Name = product.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                                Handle = product.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : "",
                                Description = product.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                                PriceInCents = product.TryGetProperty("price_in_cents", out var price) ? price.GetInt64() : 0,
                                Interval = product.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 0,
                                IntervalUnit = product.TryGetProperty("interval_unit", out var unit) ? unit.GetString() ?? "" : "",
                                ProductFamily = productFamily
                            };
                            products.Add(prod);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetProductsAsync");
            throw;
        }

        return products;
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        var subscriptions = new List<MaxioSubscription>();
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        var subscription = ExtractSubscription(item);
                        if (subscription != null)
                        {
                            subscriptions.Add(subscription);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetCustomerSubscriptionsAsync");
            throw;
        }

        return subscriptions;
    }

    private static MaxioApiCustomer? ExtractCustomer(JsonElement customerElement)
    {
        return new MaxioApiCustomer
        {
            MaxioId = customerElement.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            FirstName = customerElement.TryGetProperty("first_name", out var firstName) ? firstName.GetString() ?? "" : "",
            LastName = customerElement.TryGetProperty("last_name", out var lastName) ? lastName.GetString() ?? "" : "",
            Email = customerElement.TryGetProperty("email", out var email) ? email.GetString() ?? "" : "",
            Reference = customerElement.TryGetProperty("reference", out var reference) ? reference.GetString() ?? "" : "",
        };
    }

    private static MaxioSubscription? ExtractSubscription(JsonElement subscriptionElement)
    {
        if (subscriptionElement.ValueKind == JsonValueKind.Object &&
            subscriptionElement.TryGetProperty("subscription", out var sub))
        {
            subscriptionElement = sub;
        }

        var product = new MaxioProduct();
        if (subscriptionElement.TryGetProperty("product", out var productElement))
        {
            product = ExtractProduct(productElement);
        }

        return new MaxioSubscription
        {
            Id = subscriptionElement.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            State = subscriptionElement.TryGetProperty("state", out var state) ? state.GetString() ?? "" : "",
            CustomerId = subscriptionElement.TryGetProperty("customer_id", out var customerId) ? customerId.GetInt32() : 0,
            Product = product,
            CurrentPeriodEndsAt = ParseDateTime(subscriptionElement, "current_period_ends_at"),
            NextAssessmentAt = ParseDateTime(subscriptionElement, "next_assessment_at"),
            CreatedAt = ParseDateTime(subscriptionElement, "created_at"),
            UpdatedAt = ParseDateTime(subscriptionElement, "updated_at")
        };
    }

    private static MaxioProduct ExtractProduct(JsonElement productElement)
    {
        var productFamily = new MaxioProductFamily();
        if (productElement.TryGetProperty("product_family", out var family))
        {
            productFamily = ExtractProductFamily(family);
        }

        return new MaxioProduct
        {
            Id = productElement.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Name = productElement.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Handle = productElement.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : "",
            Description = productElement.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
            PriceInCents = productElement.TryGetProperty("price_in_cents", out var price) ? price.GetInt64() : 0,
            Interval = productElement.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 0,
            IntervalUnit = productElement.TryGetProperty("interval_unit", out var unit) ? unit.GetString() ?? "" : "",
            ProductFamily = productFamily
        };
    }

    private static MaxioProductFamily ExtractProductFamily(JsonElement familyElement)
    {
        return new MaxioProductFamily
        {
            Id = familyElement.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Name = familyElement.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Handle = familyElement.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : ""
        };
    }

    private static DateTime ParseDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(prop.GetString(), out var dt))
            {
                return dt;
            }
        }
        return DateTime.MinValue;
    }
}

public class MaxioApiCustomer
{
    public int MaxioId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public MaxioProduct Product { get; set; } = new();
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
