using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _subdomain;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey not configured");
        _subdomain = configuration["Maxio:Subdomain"] ?? "cp-exp-2";
        _logger = logger;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:x")));
    }

    private string GetBaseUrl() => $"https://{_subdomain}.chargify.com";

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{GetBaseUrl()}/products.json";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            var productsResponse = JsonSerializer.Deserialize<ProductsResponse>(json, options);

            var plans = productsResponse?.Products?.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name ?? string.Empty,
                Handle = p.Handle ?? string.Empty,
                PriceInCents = p.PriceInCents ?? 0,
                Interval = p.Interval ?? 1,
                IntervalUnit = p.IntervalUnit ?? "month"
            }).ToList() ?? new List<SubscriptionPlanDto>();

            return plans;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch subscription plans from Maxio");
            throw new MaxioException("Failed to fetch subscription plans", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse subscription plans response from Maxio");
            throw new MaxioException("Failed to parse subscription plans response", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId,
        string userEmail,
        string firstName,
        string lastName,
        int productId,
        CancellationToken ct = default)
    {
        try
        {
            var customerRef = userId;
            var subscriptionRef = $"{userId}-{productId}";

            Customer? customer = null;
            try
            {
                var customerUrl = $"{GetBaseUrl()}/customers/lookup.json?reference={customerRef}";
                var response = await _httpClient.GetAsync(customerUrl, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                    var customerResponse = JsonSerializer.Deserialize<CustomerResponse>(json, options);
                    customer = customerResponse?.Customer;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to lookup customer by reference");
            }

            if (customer is null)
            {
                _logger.LogInformation("Customer not found for reference {CustomerRef}, creating new customer", customerRef);
                var createCustomerRequest = new { customer = new { first_name = firstName, last_name = lastName, email = userEmail, reference = customerRef } };
                var content = new StringContent(JsonSerializer.Serialize(createCustomerRequest), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{GetBaseUrl()}/customers.json", content, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                var customerResponse = JsonSerializer.Deserialize<CustomerResponse>(json, options);
                customer = customerResponse?.Customer ?? throw new MaxioException("Customer response is null");
            }

            if (customer?.Id is null)
            {
                throw new MaxioException("Customer ID is null after creation/lookup");
            }

            var createSubscriptionRequest = new { subscription = new { customer_id = customer.Id, product_id = productId, reference = subscriptionRef } };
            var subContent = new StringContent(JsonSerializer.Serialize(createSubscriptionRequest), Encoding.UTF8, "application/json");
            var subResponse = await _httpClient.PostAsync($"{GetBaseUrl()}/subscriptions.json", subContent, ct);
            subResponse.EnsureSuccessStatusCode();

            var subJson = await subResponse.Content.ReadAsStringAsync(ct);
            var subOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            var subscriptionResponse = JsonSerializer.Deserialize<SubscriptionResponse>(subJson, subOptions);
            var subscription = subscriptionResponse?.Subscription ?? throw new MaxioException("Subscription is null after creation");

            return new SubscriptionDto
            {
                Id = subscription.Id ?? 0,
                CustomerId = subscription.CustomerId ?? 0,
                ProductId = subscription.ProductId ?? 0,
                State = subscription.State ?? "unknown",
                NextBillingDate = subscription.NextAssessmentAt,
                Reference = subscription.Reference ?? string.Empty,
                ActivatedAt = subscription.ActivatedAt,
                CanceledAt = subscription.CanceledAt
            };
        }
        catch (MaxioException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error during subscription creation");
            throw new MaxioException("Subscription creation failed", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse subscription creation response from Maxio");
            throw new MaxioException("Failed to parse subscription creation response", ex);
        }
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            var customerRef = userId;

            Customer? customer = null;
            try
            {
                var customerUrl = $"{GetBaseUrl()}/customers/lookup.json?reference={customerRef}";
                var response = await _httpClient.GetAsync(customerUrl, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                    var customerResponse = JsonSerializer.Deserialize<CustomerResponse>(json, options);
                    customer = customerResponse?.Customer;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to lookup customer by reference");
            }

            if (customer?.Id is null)
            {
                return new List<SubscriptionDto>();
            }

            var subscriptions = new List<SubscriptionDto>();
            int page = 1;
            const int pageSize = 50;

            while (true)
            {
                try
                {
                    var url = $"{GetBaseUrl()}/customers/{customer.Id}/subscriptions.json?page={page}&per_page={pageSize}";
                    var response = await _httpClient.GetAsync(url, ct);
                    if (!response.IsSuccessStatusCode) break;

                    var json = await response.Content.ReadAsStringAsync(ct);
                    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                    var subListResponse = JsonSerializer.Deserialize<SubscriptionListResponse>(json, options);

                    if (subListResponse?.Subscriptions?.Count == 0) break;

                    foreach (var sub in subListResponse?.Subscriptions ?? new List<Subscription>())
                    {
                        subscriptions.Add(new SubscriptionDto
                        {
                            Id = sub.Id ?? 0,
                            CustomerId = sub.CustomerId ?? 0,
                            ProductId = sub.ProductId ?? 0,
                            State = sub.State ?? "unknown",
                            NextBillingDate = sub.NextAssessmentAt,
                            Reference = sub.Reference ?? string.Empty,
                            ActivatedAt = sub.ActivatedAt,
                            CanceledAt = sub.CanceledAt
                        });
                    }

                    if ((subListResponse?.Subscriptions?.Count ?? 0) < pageSize) break;
                    page++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch subscriptions from Maxio");
                    throw new MaxioException("Failed to fetch subscriptions", ex);
                }
            }

            return subscriptions;
        }
        catch (MaxioException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse subscriptions response from Maxio");
            throw new MaxioException("Failed to parse subscriptions response", ex);
        }
    }
}

public class MaxioException : Exception
{
    public MaxioException(string message) : base(message) { }
    public MaxioException(string message, Exception innerException) : base(message, innerException) { }
}

public class SubscriptionPlanDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public decimal Price => PriceInCents / 100m;
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}

// JSON response models
internal class ProductsResponse { [JsonPropertyName("products")] public List<Product>? Products { get; set; } }
internal class Product { public long Id { get; set; } public string? Name { get; set; } public string? Handle { get; set; } public long? PriceInCents { get; set; } public int? Interval { get; set; } public string? IntervalUnit { get; set; } }
internal class CustomerResponse { [JsonPropertyName("customer")] public Customer? Customer { get; set; } }
internal class Customer { public long? Id { get; set; } public string? Email { get; set; } public string? FirstName { get; set; } public string? LastName { get; set; } public string? Reference { get; set; } }
internal class SubscriptionResponse { [JsonPropertyName("subscription")] public Subscription? Subscription { get; set; } }
internal class SubscriptionListResponse { [JsonPropertyName("subscriptions")] public List<Subscription>? Subscriptions { get; set; } }
internal class Subscription { public long? Id { get; set; } public string? State { get; set; } public long? CustomerId { get; set; } public long? ProductId { get; set; } public DateTimeOffset? NextAssessmentAt { get; set; } public DateTimeOffset? ActivatedAt { get; set; } public DateTimeOffset? CanceledAt { get; set; } public string? Reference { get; set; } }
