using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioApiClient(
        HttpClient httpClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = _settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<List<PlanDto>> GetPlansAsync(string productFamilyHandle)
    {
        _logger.LogInformation("Fetching plans for product family: {Handle}", productFamilyHandle);

        // Use top-level products endpoint and filter by family handle
        var response = await _httpClient.GetAsync("/products.json");
        response.EnsureSuccessStatusCode();

        // Maxio returns [{ "product": {...} }, ...] array
        var rawJson = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("Raw products response: {Json}", rawJson.Length > 500 ? rawJson[..500] + "..." : rawJson);

        var rawArray = JsonSerializer.Deserialize<List<ProductEnvelope>>(rawJson, _jsonOptions);
        var plans = new List<PlanDto>();

        if (rawArray != null)
        {
            foreach (var envelope in rawArray)
            {
                var product = envelope.Product;
                if (product?.ProductFamily?.Handle == productFamilyHandle)
                {
                    plans.Add(new PlanDto
                    {
                        Id = product.Id,
                        Name = product.Name,
                        Handle = product.Handle,
                        Description = product.Description ?? string.Empty,
                        PriceInCents = product.PriceInCents,
                        Interval = product.Interval.ToString(),
                        IntervalUnit = product.IntervalUnit
                    });
                }
            }
        }

        _logger.LogInformation("Found {Count} plans for family {Handle}", plans.Count, productFamilyHandle);
        return plans;
    }

    public async Task<CustomerDto?> FindCustomerByReferenceAsync(string reference)
    {
        _logger.LogInformation("Looking up customer by reference: {Reference}", reference);

        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var wrapper = await response.Content.ReadFromJsonAsync<CustomerWrapper>(_jsonOptions);
            if (wrapper?.Customer == null)
                return null;

            return MapCustomer(wrapper.Customer);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CustomerDto> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        _logger.LogInformation("Creating customer: {Reference} ({Email})", reference, email);

        var body = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var response = await _httpClient.PostAsJsonAsync("/customers.json", body, _jsonOptions);
        response.EnsureSuccessStatusCode();

        var wrapper = await response.Content.ReadFromJsonAsync<CustomerWrapper>(_jsonOptions);
        return MapCustomer(wrapper!.Customer!);
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle, string uniquenessToken)
    {
        _logger.LogInformation("Creating subscription: CustomerId={CustomerId}, Product={Product}, Token={Token}",
            customerId, productHandle, uniquenessToken);

        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                payment_collection_method = "remittance",
                uniqueness_token = uniquenessToken
            }
        };

        var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", body, _jsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio subscription creation failed: {StatusCode} - {Body}", response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        var wrapper = await response.Content.ReadFromJsonAsync<SubscriptionWrapper>(_jsonOptions);
        return MapSubscription(wrapper!.Subscription!);
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId)
    {
        _logger.LogInformation("Fetching subscriptions for customer: {CustomerId}", customerId);

        var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
        response.EnsureSuccessStatusCode();

        var wrapper = await response.Content.ReadFromJsonAsync<SubscriptionsWrapper>(_jsonOptions);
        var subscriptions = new List<SubscriptionDto>();

        if (wrapper?.Subscriptions != null)
        {
            foreach (var sub in wrapper.Subscriptions)
            {
                subscriptions.Add(MapSubscription(sub));
            }
        }

        return subscriptions;
    }

    private static CustomerDto MapCustomer(MaxioCustomer c) => new()
    {
        Id = c.Id,
        Email = c.Email,
        FirstName = c.FirstName,
        LastName = c.LastName,
        Reference = c.Reference
    };

    private static SubscriptionDto MapSubscription(MaxioSubscription s) => new()
    {
        Id = s.Id,
        State = s.State,
        PriceInCents = s.ProductPriceInCents,
        ProductHandle = s.Product?.Handle ?? string.Empty,
        ProductName = s.Product?.Name ?? string.Empty,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextAssessmentAt = s.NextAssessmentAt,
        ActivatedAt = s.ActivatedAt,
        CanceledAt = s.CanceledAt,
        CreatedAt = s.CreatedAt
    };

    #region JSON DTOs (Maxio API response shapes)

    private class ProductEnvelope
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }
    }

    private class ProductFamilyWrapper
    {
        [JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private class ProductFamilyEnvelope
    {
        [JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private class MaxioProductFamily
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Handle { get; set; } = string.Empty;
    }

    private class ProductsWrapper
    {
        [JsonPropertyName("product")]
        public MaxioProduct? Product { get; set; }

        [JsonPropertyName("products")]
        public List<MaxioProduct>? Products { get; set; }
    }

    private class MaxioProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Handle { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int PriceInCents { get; set; }
        public int Interval { get; set; }
        public string IntervalUnit { get; set; } = string.Empty;

        [JsonPropertyName("product_family")]
        public MaxioProductFamily? ProductFamily { get; set; }
    }

    private class CustomerWrapper
    {
        [JsonPropertyName("customer")]
        public MaxioCustomer? Customer { get; set; }
    }

    private class MaxioCustomer
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Reference { get; set; }
    }

    private class SubscriptionWrapper
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }
    }

    private class SubscriptionsWrapper
    {
        [JsonPropertyName("subscription")]
        public MaxioSubscription? Subscription { get; set; }

        [JsonPropertyName("subscriptions")]
        public List<MaxioSubscription>? Subscriptions { get; set; }
    }

    private class MaxioSubscription
    {
        public int Id { get; set; }
        public string State { get; set; } = string.Empty;
        public int ProductPriceInCents { get; set; }
        public MaxioProduct? Product { get; set; }
        public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
        public DateTimeOffset? CanceledAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    #endregion
}
