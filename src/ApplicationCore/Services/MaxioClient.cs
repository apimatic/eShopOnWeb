using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioClient
{
    Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<List<MaxioProduct>> ListProductsByFamilyAsync(string familyHandle);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly IAppLogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, MaxioConfiguration config, IAppLogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_config.GetBaseUrl());
        _httpClient.DefaultRequestHeaders.Add("Authorization", GetBasicAuthHeader());
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try {
            var existingCustomer = await FindCustomerByReferenceAsync(userId);
            if (existingCustomer != null)
            {
                _logger.LogInformation("Found existing Maxio customer for userId {userId}", userId);
                return existingCustomer;
            }

            var createRequest = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var json = JsonSerializer.Serialize(createRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/customers.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create Maxio customer: {statusCode} {error}", response.StatusCode, errorContent);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var customerEl = doc.RootElement.GetProperty("customer");
            var customerId = customerEl.GetProperty("id").GetInt32();
            var maxioEmail = customerEl.GetProperty("email").GetString() ?? email;

            _logger.LogInformation("Created Maxio customer {customerId} for userId {userId}", customerId, userId);
            return new MaxioCustomer { Id = customerId, Email = maxioEmail, Reference = userId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetOrCreateCustomerAsync");
            throw;
        }
    }

    public async Task<List<MaxioProduct>> ListProductsByFamilyAsync(string familyHandle)
    {
        try {
            var response = await _httpClient.GetAsync($"/products.json?filter[product_family_handle]={familyHandle}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to list products: {statusCode} {error}", response.StatusCode, errorContent);
                return new List<MaxioProduct>();
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var products = new List<MaxioProduct>();

            if (doc.RootElement.TryGetProperty("products", out var productsEl))
            {
                foreach (var productEl in productsEl.EnumerateArray())
                {
                    var product = new MaxioProduct
                    {
                        Id = productEl.GetProperty("id").GetInt32(),
                        Name = productEl.GetProperty("name").GetString() ?? "",
                        Handle = productEl.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : "",
                        PriceInCents = productEl.GetProperty("price_in_cents").GetInt64()
                    };
                    products.Add(product);
                }
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ListProductsByFamilyAsync");
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try {
            var createRequest = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle
                }
            };

            var json = JsonSerializer.Serialize(createRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create subscription: {statusCode} {error}", response.StatusCode, errorContent);
                throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var subscriptionEl = doc.RootElement.GetProperty("subscription");

            var subscription = new MaxioSubscription
            {
                Id = subscriptionEl.GetProperty("id").GetInt32(),
                State = subscriptionEl.GetProperty("state").GetString() ?? "unknown",
                ProductHandle = productHandle,
                ProductName = subscriptionEl.TryGetProperty("product", out var product) && product.TryGetProperty("name", out var name)
                    ? name.GetString() ?? ""
                    : "",
                PriceInCents = subscriptionEl.TryGetProperty("product_price_in_cents", out var price)
                    ? price.GetInt64()
                    : 0,
                CurrentPeriodStartsAt = ParseDateTime(subscriptionEl, "current_period_started_at"),
                CurrentPeriodEndsAt = ParseDateTime(subscriptionEl, "current_period_ends_at")
            };

            _logger.LogInformation("Created Maxio subscription {subscriptionId} for customer {customerId}", subscription.Id, customerId);
            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateSubscriptionAsync");
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try {
            var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={customerId}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to list subscriptions: {statusCode} {error}", response.StatusCode, errorContent);
                return new List<MaxioSubscription>();
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var subscriptions = new List<MaxioSubscription>();

            if (doc.RootElement.TryGetProperty("subscriptions", out var subsEl))
            {
                foreach (var subEl in subsEl.EnumerateArray())
                {
                    var subscription = new MaxioSubscription
                    {
                        Id = subEl.GetProperty("id").GetInt32(),
                        State = subEl.GetProperty("state").GetString() ?? "unknown",
                        ProductHandle = subEl.TryGetProperty("product", out var product) && product.TryGetProperty("handle", out var handle)
                            ? handle.GetString() ?? ""
                            : "",
                        ProductName = subEl.TryGetProperty("product", out var prod) && prod.TryGetProperty("name", out var name)
                            ? name.GetString() ?? ""
                            : "",
                        PriceInCents = subEl.TryGetProperty("product_price_in_cents", out var price)
                            ? price.GetInt64()
                            : 0,
                        CurrentPeriodStartsAt = ParseDateTime(subEl, "current_period_started_at"),
                        CurrentPeriodEndsAt = ParseDateTime(subEl, "current_period_ends_at")
                    };
                    subscriptions.Add(subscription);
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ListCustomerSubscriptionsAsync");
            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        try {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={reference}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);

            if (doc.RootElement.TryGetProperty("customer", out var customerEl))
            {
                return new MaxioCustomer
                {
                    Id = customerEl.GetProperty("id").GetInt32(),
                    Email = customerEl.GetProperty("email").GetString() ?? "",
                    Reference = customerEl.GetProperty("reference").GetString() ?? ""
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FindCustomerByReferenceAsync");
            return null;
        }
    }

    private string GetBasicAuthHeader()
    {
        var credentials = $"{_config.ApiKey}:X";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
        return $"Basic {encoded}";
    }

    private DateTime? ParseDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var dateEl) && dateEl.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(dateEl.GetString(), out var dt))
            {
                return dt;
            }
        }
        return null;
    }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
