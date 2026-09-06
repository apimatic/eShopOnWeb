using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<List<MaxioProduct>> ListProductsAsync();
    Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
    Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var baseUrl = _settings.BaseUrl ?? $"https://{_settings.Subdomain}.maxio.com/api/v1";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try {
            var existingCustomer = await FindCustomerByReferenceAsync(userId);
            if (existingCustomer != null)
            {
                _logger.LogInformation($"Found existing Maxio customer for user {userId}: {existingCustomer.Id}");
                return existingCustomer;
            }

            _logger.LogInformation($"Creating new Maxio customer for user {userId}");
            var requestBody = new {
                customer = new {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var response = await _httpClient.PostAsync(
                "/customers.json",
                new StringContent(JsonSerializer.Serialize(requestBody, _jsonOptions), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create customer: {response.StatusCode} - {content}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("customer", out var customerElement))
            {
                var customer = JsonSerializer.Deserialize<MaxioCustomer>(customerElement.GetRawText(), _jsonOptions);
                _logger.LogInformation($"Successfully created Maxio customer: {customer?.Id}");
                return customer;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating customer for user {userId}");
            return null;
        }
    }

    public async Task<List<MaxioProduct>> ListProductsAsync()
    {
        try {
            var response = await _httpClient.GetAsync($"/products.json?product_family_id={await GetProductFamilyIdAsync()}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to list products: {response.StatusCode}");
                return new List<MaxioProduct>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var products = new List<MaxioProduct>();

            if (doc.RootElement.TryGetProperty("products", out var productsArray))
            {
                foreach (var element in productsArray.EnumerateArray())
                {
                    var product = JsonSerializer.Deserialize<MaxioProduct>(element.GetRawText(), _jsonOptions);
                    if (product != null)
                    {
                        products.Add(product);
                    }
                }
            }

            _logger.LogInformation($"Retrieved {products.Count} products from Maxio");
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products");
            return new List<MaxioProduct>();
        }
    }

    public async Task<MaxioSubscription?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try {
            var requestBody = new {
                subscription = new {
                    customer_id = customerId,
                    product_handle = productHandle
                }
            };

            var response = await _httpClient.PostAsync(
                "/subscriptions.json",
                new StringContent(JsonSerializer.Serialize(requestBody, _jsonOptions), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to create subscription: {response.StatusCode} - {content}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("subscription", out var subscriptionElement))
            {
                var subscription = JsonSerializer.Deserialize<MaxioSubscription>(subscriptionElement.GetRawText(), _jsonOptions);
                _logger.LogInformation($"Successfully created subscription: {subscription?.Id}");
                return subscription;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating subscription for customer {customerId}");
            return null;
        }
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try {
            var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={customerId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to list subscriptions: {response.StatusCode}");
                return new List<MaxioSubscription>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var subscriptions = new List<MaxioSubscription>();

            if (doc.RootElement.TryGetProperty("subscriptions", out var subscriptionsArray))
            {
                foreach (var element in subscriptionsArray.EnumerateArray())
                {
                    var subscription = JsonSerializer.Deserialize<MaxioSubscription>(element.GetRawText(), _jsonOptions);
                    if (subscription != null)
                    {
                        subscriptions.Add(subscription);
                    }
                }
            }

            _logger.LogInformation($"Retrieved {subscriptions.Count} subscriptions for customer {customerId}");
            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error listing subscriptions for customer {customerId}");
            return new List<MaxioSubscription>();
        }
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId)
    {
        try {
            var response = await _httpClient.GetAsync($"/subscriptions/{subscriptionId}.json");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to get subscription {subscriptionId}: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("subscription", out var subscriptionElement))
            {
                var subscription = JsonSerializer.Deserialize<MaxioSubscription>(subscriptionElement.GetRawText(), _jsonOptions);
                return subscription;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting subscription {subscriptionId}");
            return null;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        try {
            var response = await _httpClient.GetAsync($"/customers.json?reference={Uri.EscapeDataString(reference)}");

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("customers", out var customersArray))
            {
                foreach (var element in customersArray.EnumerateArray())
                {
                    var customer = JsonSerializer.Deserialize<MaxioCustomer>(element.GetRawText(), _jsonOptions);
                    if (customer != null && customer.Reference == reference)
                    {
                        return customer;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error finding customer by reference {reference}");
            return null;
        }
    }

    private async Task<int> GetProductFamilyIdAsync()
    {
        try {
            var response = await _httpClient.GetAsync("/product_families.json");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Failed to list product families: {response.StatusCode}");
                return 0;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("product_families", out var familiesArray))
            {
                foreach (var element in familiesArray.EnumerateArray())
                {
                    if (element.TryGetProperty("product_family", out var familyElement))
                    {
                        var family = JsonSerializer.Deserialize<MaxioProductFamily>(familyElement.GetRawText(), _jsonOptions);
                        if (family?.Handle == _settings.ProductFamilyHandle)
                        {
                            return family.Id;
                        }
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product family");
            return 0;
        }
    }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int ProductFamilyId { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime NextBillingAt { get; set; }
    public string? TrialEndsAt { get; set; }
}
