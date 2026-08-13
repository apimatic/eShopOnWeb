using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _subdomain;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MaxioService(HttpClient httpClient, IConfiguration configuration, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _apiKey = configuration["Maxio:ApiKey"]
            ?? throw new InvalidOperationException("Maxio:ApiKey not configured");
        _subdomain = configuration["Maxio:Subdomain"]
            ?? throw new InvalidOperationException("Maxio:Subdomain not configured");
        _productFamilyHandle = configuration["Maxio:ProductFamilyHandle"]
            ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle not configured");

        var baseUrl = configuration["Maxio:BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
        {
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
        else
        {
            _httpClient.BaseAddress = new Uri($"https://{_subdomain}.chargify.com");
        }

        SetAuthorizationHeader();
    }

    private void SetAuthorizationHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<MaxioPlan[]> GetSubscriptionPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/product_families/{productFamilyHandle}/products.json";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var products = doc.RootElement.GetProperty("products");

            var plans = new List<MaxioPlan>();

            foreach (var product in products.EnumerateArray())
            {
                var plan = new MaxioPlan
                {
                    Id = product.GetProperty("id").GetInt64(),
                    Handle = product.GetProperty("handle").GetString() ?? "",
                    Name = product.GetProperty("name").GetString() ?? "",
                    Price = GetDecimalValue(product, "priceInCents") / 100,
                    Description = product.TryGetProperty("description", out var desc) ? desc.GetString() : null
                };
                plans.Add(plan);
            }

            return plans.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio for family {FamilyHandle}", productFamilyHandle);
            throw;
        }
    }

    public async Task<MaxioCustomer> GetOrCreateMaxioCustomerAsync(string userId, string email, CancellationToken cancellationToken = default)
    {
        try
        {
            var customerRef = GenerateCustomerReference(userId);

            // First try to find existing customer by reference
            var existing = await FindCustomerByReferenceAsync(customerRef, cancellationToken);
            if (existing != null)
            {
                return existing;
            }

            // Create new customer
            var createPayload = new
            {
                customer = new
                {
                    firstName = "User",
                    lastName = userId.Substring(0, Math.Min(10, userId.Length)),
                    email = email,
                    reference = customerRef
                }
            };

            var json = JsonSerializer.Serialize(createPayload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/customers.json", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);
            var customer = doc.RootElement.GetProperty("customer");

            return new MaxioCustomer
            {
                Id = customer.GetProperty("id").GetInt64(),
                Reference = customer.GetProperty("reference").GetString() ?? "",
                Email = customer.GetProperty("email").GetString() ?? "",
                FirstName = customer.TryGetProperty("firstName", out var fn) ? fn.GetString() : null,
                LastName = customer.TryGetProperty("lastName", out var ln) ? ln.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating Maxio customer for userId {UserId}", userId);
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string userId, string planHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await GetOrCreateMaxioCustomerAsync(userId, $"{userId}@eshop.local", cancellationToken);

            var createPayload = new
            {
                subscription = new
                {
                    customerId = customer.Id,
                    productHandle = planHandle,
                    autoResume = true
                }
            };

            var json = JsonSerializer.Serialize(createPayload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);
            var subscription = doc.RootElement.GetProperty("subscription");

            return MapSubscription(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for userId {UserId}, plan {PlanHandle}", userId, planHandle);
            throw;
        }
    }

    public async Task<MaxioSubscription[]> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var customerRef = GenerateCustomerReference(userId);
            var customer = await FindCustomerByReferenceAsync(customerRef, cancellationToken);

            if (customer == null)
            {
                return Array.Empty<MaxioSubscription>();
            }

            var url = $"/customers/{customer.Id}/subscriptions.json";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var subscriptions = doc.RootElement.GetProperty("subscriptions");

            var result = new List<MaxioSubscription>();
            foreach (var sub in subscriptions.EnumerateArray())
            {
                result.Add(MapSubscription(sub));
            }

            return result.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscriptions for userId {UserId}", userId);
            throw;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            // Search for customer by reference - this uses query parameters
            var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                throw new HttpRequestException($"Failed to lookup customer: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var customer = doc.RootElement.GetProperty("customer");

            return new MaxioCustomer
            {
                Id = customer.GetProperty("id").GetInt64(),
                Reference = customer.GetProperty("reference").GetString() ?? "",
                Email = customer.GetProperty("email").GetString() ?? "",
                FirstName = customer.TryGetProperty("firstName", out var fn) ? fn.GetString() : null,
                LastName = customer.TryGetProperty("lastName", out var ln) ? ln.GetString() : null
            };
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private MaxioSubscription MapSubscription(JsonElement sub)
    {
        return new MaxioSubscription
        {
            Id = sub.GetProperty("id").GetInt64(),
            CustomerId = sub.GetProperty("customerId").GetInt64(),
            ProductId = sub.GetProperty("productId").GetInt64(),
            PlanId = sub.TryGetProperty("productPricePointId", out var ppId) ? ppId.GetInt32() : null,
            PlanHandle = GetStringValue(sub, "productHandle", "") ?? "",
            Price = TryGetDecimalValue(sub, "currentPrice"),
            State = GetStringValue(sub, "state", "unknown") ?? "unknown",
            CurrentPeriodStartsAt = GetDateTimeValue(sub, "currentPeriodStartsAt"),
            CurrentPeriodEndsAt = TryGetDateTimeValue(sub, "currentPeriodEndsAt"),
            NextBillingAt = TryGetDateTimeValue(sub, "nextBillingAt")
        };
    }

    private string GenerateCustomerReference(string userId)
    {
        return $"eshop-{userId.Substring(0, Math.Min(30, userId.Length))}";
    }

    private static string? GetStringValue(JsonElement element, string propertyName, string? defaultValue = null)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            return prop.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static decimal GetDecimalValue(JsonElement element, string propertyName, decimal defaultValue = 0)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            return prop.GetDecimal();
        }
        return defaultValue;
    }

    private static decimal? TryGetDecimalValue(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            return prop.GetDecimal();
        }
        return null;
    }

    private static DateTime GetDateTimeValue(JsonElement element, string propertyName, DateTime? defaultValue = null)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            return prop.GetDateTime();
        }
        return defaultValue ?? DateTime.UtcNow;
    }

    private static DateTime? TryGetDateTimeValue(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            try
            {
                return prop.GetDateTime();
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
}
