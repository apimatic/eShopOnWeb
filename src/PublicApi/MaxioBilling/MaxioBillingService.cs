using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptionsMonitor<MaxioSettings> optionsMonitor, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = optionsMonitor.Get(MaxioSettings.CONFIG_NAME);
        _logger = logger;
    }

    public async Task<SubscriptionPlanDto[]> ListSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var familyHandle = _settings.ProductFamilyHandle;
            var url = $"/product_families/handle:{familyHandle}/products.json";

            var response = await GetAsync<ProductFamilyResponse>(url, cancellationToken);
            if (response?.Items == null) return [];

            return response.Items
                .Where(r => r.Product != null)
                .Select(r => new SubscriptionPlanDto
                {
                    Id = r.Product!.Id,
                    Handle = r.Product.Handle ?? string.Empty,
                    Name = r.Product.Name,
                    Description = r.Product.Description,
                    PriceInCents = r.Product.PriceInCents,
                    Interval = r.Product.Interval,
                    IntervalUnit = r.Product.IntervalUnit,
                    RequireCreditCard = r.Product.RequireCreditCard
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscription plans");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string productHandle, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await EnsureCustomerExistsAsync(userId, cancellationToken);

            var request = new
            {
                subscription = new
                {
                    customer_id = customer.Id,
                    product_handle = productHandle,
                    skip_billing_manifest_validation = true
                }
            };

            var response = await PostAsync<SubscriptionResponse>("/subscriptions.json", request, cancellationToken);
            return MapSubscriptionResponse(response?.Subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId} with product {ProductHandle}", userId, productHandle);
            throw;
        }
    }

    public async Task<SubscriptionDto[]> ListUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await GetOrNullCustomerByReferenceAsync(userId, cancellationToken);
            if (customer == null) return [];

            var url = $"/customers/{customer.Id}/subscriptions.json";
            var response = await GetAsync<CustomerSubscriptionsResponse>(url, cancellationToken);

            if (response?.Subscriptions == null) return [];

            return response.Subscriptions
                .Select(MapSubscriptionResponse)
                .Where(s => s != null)
                .ToArray()!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for user {UserId}", userId);
            throw;
        }
    }

    public async Task<SubscriptionDto> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/subscriptions/{subscriptionId}.json";
            var response = await GetAsync<SubscriptionResponse>(url, cancellationToken);
            return MapSubscriptionResponse(response?.Subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription {SubscriptionId}", subscriptionId);
            throw;
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerExistsAsync(string reference, CancellationToken cancellationToken)
    {
        var existing = await GetOrNullCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null) return existing;

        var request = new
        {
            customer = new
            {
                first_name = "Customer",
                last_name = reference,
                email = $"{reference}@eshop.local",
                reference = reference
            }
        };

        var response = await PostAsync<CustomerResponse>("/customers.json", request, cancellationToken);
        if (response?.Customer == null)
            throw new InvalidOperationException($"Failed to create customer with reference {reference}");

        return response.Customer;
    }

    private async Task<MaxioCustomer?> GetOrNullCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await GetAsync<CustomerResponse>(url, cancellationToken);
            return response?.Customer;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T?> GetAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        var url = BuildUrl(endpoint);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(content, GetJsonSerializerOptions());
    }

    private async Task<T?> PostAsync<T>(string endpoint, object body, CancellationToken cancellationToken)
    {
        var url = BuildUrl(endpoint);
        var jsonContent = JsonSerializer.Serialize(body, GetJsonSerializerOptions());

        using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        AddAuthHeader(request);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(responseContent, GetJsonSerializerOptions());
    }

    private string BuildUrl(string endpoint)
    {
        if (!string.IsNullOrEmpty(_settings.BaseUrl))
            return _settings.BaseUrl.TrimEnd('/') + endpoint;

        return $"https://{_settings.Subdomain}.chargify.com{endpoint}";
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:x"));
        request.Headers.Add("Authorization", $"Basic {credentials}");
    }

    private static SubscriptionDto? MapSubscriptionResponse(MaxioSubscription? sub)
    {
        if (sub == null) return null;

        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            CustomerId = sub.CustomerId,
            ProductId = sub.ProductId,
            ProductHandle = sub.ProductHandle ?? string.Empty,
            ProductName = sub.ProductName ?? string.Empty,
            ProductPriceInCents = sub.ProductPriceInCents ?? 0,
            CurrentPeriodStartsAt = sub.CurrentPeriodStartsAt,
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt
        };
    }

    private static JsonSerializerOptions GetJsonSerializerOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
}

#region Maxio API Response Models
internal class ProductFamilyResponse
{
    [JsonPropertyName("items")]
    public ProductResponse[]? Items { get; set; }
}

internal class ProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }
}

internal class CustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

internal class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal class CustomerSubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public MaxioSubscription[]? Subscriptions { get; set; }
}

internal class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_starts_at")]
    public DateTime CurrentPeriodStartsAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }
}
#endregion
