using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, MaxioConfiguration config, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var baseUrl = _config.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Clear();

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string externalId)
    {
        try
        {
            var customers = await SearchCustomersByReferenceAsync(externalId);
            if (customers.Count > 0)
            {
                _logger.LogInformation("Customer with reference {ExternalId} already exists (ID: {CustomerId})", externalId, customers[0].Id);
                return customers[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search for existing customers by reference {ExternalId}, will create new one", externalId);
        }

        var createRequest = new
        {
            customer = new
            {
                email,
                first_name = firstName,
                last_name = lastName,
                reference = externalId
            }
        };

        var json = JsonSerializer.Serialize(createRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("/customers.json", content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseBody);
            var customerJson = jsonDoc.RootElement.GetProperty("customer");

            return ParseCustomer(customerJson);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to create Maxio customer for email {Email}", email);
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string? productPricePointHandle = null)
    {
        var createRequest = new Dictionary<string, object>
        {
            { "subscription", new Dictionary<string, object>
                {
                    { "customer_id", customerId },
                    { "product_handle", productHandle },
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(productPricePointHandle))
        {
            ((Dictionary<string, object>)createRequest["subscription"])["product_price_point_handle"] = productPricePointHandle;
        }

        var json = JsonSerializer.Serialize(createRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseBody);
            var subscriptionJson = jsonDoc.RootElement.GetProperty("subscription");

            return ParseSubscription(subscriptionJson);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to create subscription for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<MaxioProduct>> ListProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/products.json");
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseBody);
            var productsArray = jsonDoc.RootElement.GetProperty("products");

            var products = new List<MaxioProduct>();
            foreach (var productJson in productsArray.EnumerateArray())
            {
                products.Add(ParseProduct(productJson));
            }

            return products;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to list Maxio products");
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseBody);
            var subscriptionsArray = jsonDoc.RootElement.GetProperty("subscriptions");

            var subscriptions = new List<MaxioSubscription>();
            foreach (var subJson in subscriptionsArray.EnumerateArray())
            {
                subscriptions.Add(ParseSubscription(subJson));
            }

            return subscriptions;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to list subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json");
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseBody);
            var subscriptionJson = jsonDoc.RootElement.GetProperty("subscription");

            return ParseSubscription(subscriptionJson);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get subscription {SubscriptionId}", subscriptionId);
            return null;
        }
    }

    private async Task<List<MaxioCustomer>> SearchCustomersByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers.json?reference={Uri.EscapeDataString(reference)}");
            if (!response.IsSuccessStatusCode)
            {
                return new List<MaxioCustomer>();
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseBody);

            if (!jsonDoc.RootElement.TryGetProperty("customers", out var customersArray))
            {
                return new List<MaxioCustomer>();
            }

            var customers = new List<MaxioCustomer>();
            foreach (var customerJson in customersArray.EnumerateArray())
            {
                customers.Add(ParseCustomer(customerJson));
            }

            return customers;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to search for customers by reference {Reference}", reference);
            return new List<MaxioCustomer>();
        }
    }

    private async Task<List<MaxioCustomer>> ListCustomersAsync(string email)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers.json?search={Uri.EscapeDataString(email)}");
            if (!response.IsSuccessStatusCode)
            {
                return new List<MaxioCustomer>();
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseBody);

            if (!jsonDoc.RootElement.TryGetProperty("customers", out var customersArray))
            {
                return new List<MaxioCustomer>();
            }

            var customers = new List<MaxioCustomer>();
            foreach (var customerJson in customersArray.EnumerateArray())
            {
                customers.Add(ParseCustomer(customerJson));
            }

            return customers;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to search for customers by email {Email}", email);
            return new List<MaxioCustomer>();
        }
    }

    private static MaxioCustomer ParseCustomer(JsonElement customerJson)
    {
        return new MaxioCustomer
        {
            Id = customerJson.GetProperty("id").GetInt32(),
            Email = customerJson.GetProperty("email").GetString() ?? string.Empty,
            FirstName = customerJson.GetProperty("first_name").GetString() ?? string.Empty,
            LastName = customerJson.GetProperty("last_name").GetString() ?? string.Empty,
            Reference = customerJson.TryGetProperty("reference", out var refProp)
                ? refProp.GetString() ?? string.Empty
                : string.Empty
        };
    }

    private static MaxioSubscription ParseSubscription(JsonElement subscriptionJson)
    {
        var nextBillingAtStr = subscriptionJson.TryGetProperty("next_billing_at", out var nbProp)
            ? nbProp.GetString()
            : null;
        var activatedAtStr = subscriptionJson.TryGetProperty("activated_at", out var aProp)
            ? aProp.GetString()
            : null;

        return new MaxioSubscription
        {
            Id = subscriptionJson.GetProperty("id").GetInt32(),
            CustomerId = subscriptionJson.GetProperty("customer_id").GetInt32(),
            ProductId = subscriptionJson.GetProperty("product_id").GetInt32(),
            ProductHandle = subscriptionJson.GetProperty("product_handle").GetString() ?? string.Empty,
            ProductName = subscriptionJson.GetProperty("product_name").GetString() ?? string.Empty,
            CurrentPriceInCents = (decimal)(subscriptionJson.TryGetProperty("current_price_in_cents", out var priceProp)
                ? priceProp.GetDecimal()
                : 0),
            State = subscriptionJson.GetProperty("state").GetString() ?? string.Empty,
            NextBillingAt = string.IsNullOrWhiteSpace(nextBillingAtStr) ? null : DateTime.Parse(nextBillingAtStr),
            ActivatedAt = string.IsNullOrWhiteSpace(activatedAtStr) ? null : DateTime.Parse(activatedAtStr)
        };
    }

    private static MaxioProduct ParseProduct(JsonElement productJson)
    {
        return new MaxioProduct
        {
            Id = productJson.GetProperty("id").GetInt32(),
            Name = productJson.GetProperty("name").GetString() ?? string.Empty,
            Handle = productJson.GetProperty("handle").GetString() ?? string.Empty,
            ProductFamilyId = productJson.GetProperty("product_family_id").GetInt32(),
            ProductFamilyName = productJson.GetProperty("product_family").GetProperty("name").GetString() ?? string.Empty
        };
    }
}
