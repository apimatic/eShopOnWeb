using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IConfiguration configuration, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var maxioSection = configuration.GetSection("Maxio");
        _apiKey = maxioSection["ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey is required");
        var subdomain = maxioSection["Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain is required");
        _productFamilyHandle = maxioSection["ProductFamilyHandle"] ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle is required");

        var baseUrlOverride = maxioSection["BaseUrl"];
        _baseUrl = !string.IsNullOrEmpty(baseUrlOverride)
            ? baseUrlOverride
            : $"https://{subdomain}.chargify.com";

        _logger.LogInformation("Maxio client initialized with base URL: {BaseUrl}", _baseUrl);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string email, string firstName, string lastName)
    {
        var request = new
        {
            customer = new
            {
                email,
                first_name = firstName,
                last_name = lastName
            }
        };

        var content = await PostAsync("/customers.json", request);
        using var doc = JsonDocument.Parse(content);
        var customer = doc.RootElement.GetProperty("customer");

        return new MaxioCustomer
        {
            Id = customer.GetProperty("id").GetInt32(),
            Email = customer.GetProperty("email").GetString() ?? string.Empty,
            FirstName = customer.GetProperty("first_name").GetString() ?? string.Empty,
            LastName = customer.GetProperty("last_name").GetString() ?? string.Empty
        };
    }

    public async Task<IEnumerable<SubscriptionPlan>> GetSubscriptionPlansAsync()
    {
        var plans = new List<SubscriptionPlan>();

        try
        {
            var productFamilyId = await GetProductFamilyIdAsync(_productFamilyHandle);
            if (productFamilyId == 0)
            {
                _logger.LogWarning("Product family not found for handle: {Handle}", _productFamilyHandle);
                return plans;
            }

            var content = await GetAsync($"/product_families/{productFamilyId}/products.json");
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var productWrapper in doc.RootElement.EnumerateArray())
                {
                    if (productWrapper.TryGetProperty("product", out var product))
                    {
                        if (product.TryGetProperty("id", out var idProp) &&
                            product.TryGetProperty("handle", out var handleProp) &&
                            product.TryGetProperty("name", out var nameProp) &&
                            product.TryGetProperty("price_in_cents", out var priceProp))
                        {
                            var price = priceProp.GetDecimal();
                            plans.Add(new SubscriptionPlan
                            {
                                Id = idProp.GetInt32(),
                                Handle = handleProp.GetString() ?? string.Empty,
                                Name = nameProp.GetString() ?? string.Empty,
                                Price = price / 100m,
                                ProductFamilyId = productFamilyId
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans");
            throw;
        }

        return plans;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var request = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle
            }
        };

        var content = await PostAsync("/subscriptions.json", request);
        using var doc = JsonDocument.Parse(content);
        var subscription = doc.RootElement.GetProperty("subscription");

        return MapSubscription(subscription);
    }

    public async Task<IEnumerable<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        var subscriptions = new List<MaxioSubscription>();

        try
        {
            var content = await GetAsync($"/customers/{customerId}/subscriptions.json");
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var sub in doc.RootElement.EnumerateArray())
                {
                    subscriptions.Add(MapSubscription(sub));
                }
            }
            else if (doc.RootElement.TryGetProperty("subscriptions", out var subsElement))
            {
                foreach (var sub in subsElement.EnumerateArray())
                {
                    subscriptions.Add(MapSubscription(sub));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for customer {CustomerId}", customerId);
            throw;
        }

        return subscriptions;
    }

    private MaxioSubscription MapSubscription(JsonElement sub)
    {
        var currentPriceInCents = sub.TryGetProperty("current_price_in_cents", out var priceProp)
            ? priceProp.GetDecimal()
            : 0m;

        return new MaxioSubscription
        {
            Id = sub.GetProperty("id").GetInt32(),
            CustomerId = sub.GetProperty("customer_id").GetInt32(),
            ProductId = sub.GetProperty("product_id").GetInt32(),
            ProductHandle = sub.GetProperty("product_handle").GetString() ?? string.Empty,
            State = sub.GetProperty("state").GetString() ?? string.Empty,
            CurrentPeriodStartsAt = sub.TryGetProperty("current_period_starts_at", out var startAt)
                ? startAt.GetString() ?? string.Empty
                : string.Empty,
            CurrentPeriodEndsAt = sub.TryGetProperty("current_period_ends_at", out var endAt)
                ? endAt.GetString() ?? string.Empty
                : string.Empty,
            CurrentPrice = currentPriceInCents / 100m,
            NextBillingAt = sub.TryGetProperty("next_billing_at", out var nextBilling)
                ? nextBilling.GetString() ?? string.Empty
                : string.Empty
        };
    }

    private async Task<int> GetProductFamilyIdAsync(string handle)
    {
        var content = await GetAsync("/product_families.json");
        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            // Root is an array of wrapped objects [{product_family: {...}}, ...]
            foreach (var wrapper in doc.RootElement.EnumerateArray())
            {
                if (wrapper.TryGetProperty("product_family", out var family))
                {
                    if (family.TryGetProperty("handle", out var handleProp))
                    {
                        var familyHandle = handleProp.GetString();
                        if (familyHandle == handle && family.TryGetProperty("id", out var idProp))
                        {
                            return idProp.GetInt32();
                        }
                    }
                }
                else if (wrapper.TryGetProperty("handle", out var handleProp))
                {
                    // Fallback: handle is directly on the array element
                    var familyHandle = handleProp.GetString();
                    if (familyHandle == handle && wrapper.TryGetProperty("id", out var idProp))
                    {
                        return idProp.GetInt32();
                    }
                }
            }
        }
        else if (doc.RootElement.TryGetProperty("product_family", out var familyElement))
        {
            // Single product_family object
            if (familyElement.TryGetProperty("handle", out var handleProp))
            {
                var familyHandle = handleProp.GetString();
                if (familyHandle == handle && familyElement.TryGetProperty("id", out var idProp))
                {
                    return idProp.GetInt32();
                }
            }
        }
        else if (doc.RootElement.TryGetProperty("product_families", out var familiesElement))
        {
            // Array under product_families key
            if (familiesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var family in familiesElement.EnumerateArray())
                {
                    if (family.TryGetProperty("handle", out var handleProp))
                    {
                        var familyHandle = handleProp.GetString();
                        if (familyHandle == handle && family.TryGetProperty("id", out var idProp))
                        {
                            return idProp.GetInt32();
                        }
                    }
                }
            }
        }

        return 0;
    }

    private async Task<string> GetAsync(string endpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + endpoint);
        AddAuthHeader(request);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> PostAsync(string endpoint, object data)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + endpoint);
        AddAuthHeader(request);

        var jsonContent = JsonSerializer.Serialize(data, GetJsonOptions());
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:x"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    private static JsonSerializerOptions GetJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
