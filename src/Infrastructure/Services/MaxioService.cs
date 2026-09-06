using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioService
{
    Task<MaxioCustomer?> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string reference);
    Task<List<MaxioProduct>> GetProductsAsync();
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId);
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
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<MaxioCustomer?> GetOrCreateCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        // First try to find existing customer by reference
        var existingCustomer = await FindCustomerByReferenceAsync(reference);
        if (existingCustomer != null)
        {
            _logger.LogInformation("Found existing Maxio customer for reference {Reference}: {CustomerId}", reference, existingCustomer.Id);
            return existingCustomer;
        }

        // Create new customer
        var createRequest = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference,
            }
        };

        var json = JsonSerializer.Serialize(createRequest, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await PostAsync("/customers.json", content);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create Maxio customer: {StatusCode}", response.StatusCode);
            return null;
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseContent);
        var customerData = doc.RootElement.GetProperty("customer");
        var customerId = customerData.GetProperty("id").GetInt32();

        _logger.LogInformation("Created new Maxio customer {CustomerId} for reference {Reference}", customerId, reference);

        return new MaxioCustomer
        {
            Id = customerId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
        };
    }

    public async Task<List<MaxioProduct>> GetProductsAsync()
    {
        var response = await GetAsync($"/product_families/handle:{_settings.ProductFamilyHandle}/products.json");
        var products = new List<MaxioProduct>();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch products: {StatusCode}", response.StatusCode);
            return products;
        }

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var productElement in doc.RootElement.EnumerateArray())
            {
                var product = ParseProduct(productElement);
                if (product != null)
                {
                    products.Add(product);
                }
            }
        }

        return products;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var createRequest = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "remittance"
            }
        };

        var json = JsonSerializer.Serialize(createRequest, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await PostAsync("/subscriptions.json", content);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create subscription: {StatusCode} - {Error}", response.StatusCode, errorContent);
            throw new InvalidOperationException($"Failed to create subscription: {response.StatusCode}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseContent);
        var subscriptionData = doc.RootElement.GetProperty("subscription");

        return ParseSubscription(subscriptionData) ?? throw new InvalidOperationException("Failed to parse subscription response");
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        var response = await GetAsync($"/customers/{customerId}/subscriptions.json");
        var subscriptions = new List<MaxioSubscription>();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch subscriptions: {StatusCode}", response.StatusCode);
            return subscriptions;
        }

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var subElement in doc.RootElement.EnumerateArray())
            {
                var subscription = ParseSubscription(subElement);
                if (subscription != null)
                {
                    subscriptions.Add(subscription);
                }
            }
        }

        return subscriptions;
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var response = await GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var customerData = doc.RootElement.GetProperty("customer");

        return new MaxioCustomer
        {
            Id = customerData.GetProperty("id").GetInt32(),
            Email = customerData.GetProperty("email").GetString() ?? "",
            FirstName = customerData.GetProperty("first_name").GetString() ?? "",
            LastName = customerData.GetProperty("last_name").GetString() ?? "",
        };
    }

    private MaxioProduct? ParseProduct(JsonElement productElement)
    {
        try
        {
            var id = productElement.GetProperty("id").GetInt32();
            var name = productElement.GetProperty("name").GetString() ?? "";
            var handle = productElement.GetProperty("handle").GetString() ?? "";
            var priceInCents = productElement.GetProperty("price_in_cents").GetInt32();

            return new MaxioProduct
            {
                Id = id,
                Name = name,
                Handle = handle,
                PriceInCents = priceInCents,
                IntervalUnit = productElement.GetProperty("interval_unit").GetString() ?? "month",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse product element");
            return null;
        }
    }

    private MaxioSubscription? ParseSubscription(JsonElement subElement)
    {
        try
        {
            var id = subElement.GetProperty("id").GetInt32();
            var state = subElement.GetProperty("state").GetString() ?? "";
            var productName = "";
            var productHandle = "";

            if (subElement.TryGetProperty("product", out var productProp))
            {
                productName = productProp.GetProperty("name").GetString() ?? "";
                productHandle = productProp.GetProperty("handle").GetString() ?? "";
            }

            var nextAssessmentAtStr = subElement.GetProperty("next_assessment_at").GetString();
            DateTime? nextAssessmentAt = string.IsNullOrEmpty(nextAssessmentAtStr) ? null : DateTime.Parse(nextAssessmentAtStr);

            return new MaxioSubscription
            {
                Id = id,
                State = state,
                ProductName = productName,
                ProductHandle = productHandle,
                PriceInCents = subElement.GetProperty("product_price_in_cents").GetInt32(),
                NextAssessmentAt = nextAssessmentAt,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse subscription element");
            return null;
        }
    }

    private async Task<HttpResponseMessage> GetAsync(string path)
    {
        var url = _settings.GetBaseUrl() + path;
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeaders(request);
        return await _httpClient.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostAsync(string path, HttpContent content)
    {
        var url = _settings.GetBaseUrl() + path;
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        AddAuthHeaders(request);
        return await _httpClient.SendAsync(request);
    }

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
}
