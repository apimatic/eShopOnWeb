using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioService : IMaxioService
{
    private readonly ILogger<MaxioService> _logger;
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly string _authHeader;

    public MaxioService(ILogger<MaxioService> logger, IOptions<MaxioSettings> settings, HttpClient httpClient)
    {
        _logger = logger;
        _settings = settings.Value;
        _httpClient = httpClient;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _authHeader = $"Basic {credentials}";

        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        }
        else
        {
            _httpClient.BaseAddress = new Uri($"https://{_settings.Subdomain}.chargify.com/");
        }

        _httpClient.DefaultRequestHeaders.Add("Authorization", _authHeader);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<GetSubscriptionPlansResponse> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching subscription plans for family: {ProductFamilyHandle}", _settings.ProductFamilyHandle);

        try
        {
            var result = new GetSubscriptionPlansResponse();
            var response = await _httpClient.GetAsync("products.json", cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Maxio API Response Status: {StatusCode}", response.StatusCode);
            _logger.LogInformation("Maxio API Response Body: {ResponseBody}", responseBody.Substring(0, Math.Min(500, responseBody.Length)));

            response.EnsureSuccessStatusCode();

            var products = System.Text.Json.JsonSerializer.Deserialize<ProductsListResponse>(responseBody,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (products?.Products != null)
            {
                foreach (var product in products.Products)
                {
                    if (product.ProductFamily?.Handle == _settings.ProductFamilyHandle)
                    {
                        var plan = new SubscriptionPlanDto
                        {
                            Id = product.Id ?? 0,
                            Handle = product.Handle ?? string.Empty,
                            Name = product.Name ?? string.Empty,
                            Price = ConvertCentsToDecimal(product.PriceInCents ?? 0),
                            Description = product.Description ?? string.Empty,
                            IntervalValue = product.Interval ?? 1,
                            IntervalUnit = product.IntervalUnit ?? "month"
                        };

                        result.Plans.Add(plan);
                    }
                }
            }

            _logger.LogInformation("Successfully fetched {PlanCount} subscription plans", result.Plans.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans");
            throw;
        }
    }

    public async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating subscription for user {UserId} with plan {PlanHandle}", userId, planHandle);

        try
        {
            var customerId = await EnsureCustomerExistsAsync(userId, email, firstName, lastName, cancellationToken);

            var subscriptionRequest = new { subscription = new { customer_id = customerId, product_handle = planHandle, payment_collection_method = "remittance" } };

            var response = await _httpClient.PostAsJsonAsync("subscriptions.json", subscriptionRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var subscriptionData = await response.Content.ReadFromJsonAsync<SubscriptionResponseWrapper>(cancellationToken);
            var subscription = subscriptionData.Subscription;

            var result = new CreateSubscriptionResponse
            {
                SubscriptionId = subscription.Id ?? 0,
                CustomerId = subscription.CustomerId ?? 0,
                State = subscription.State ?? string.Empty,
                ActivatedAt = subscription.ActivatedAt ?? DateTime.UtcNow,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                Price = ConvertCentsToDecimal(subscription.ProductPriceInCents ?? 0),
                PlanHandle = planHandle
            };

            _logger.LogInformation("Successfully created subscription {SubscriptionId} for customer {CustomerId}", result.SubscriptionId, result.CustomerId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    public async Task<GetUserSubscriptionsResponse> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching subscriptions for user {UserId}", userId);

        try
        {
            var result = new GetUserSubscriptionsResponse();

            var customer = await GetCustomerByReferenceAsync(userId, cancellationToken);
            if (customer == null)
            {
                _logger.LogInformation("No customer found for user {UserId}", userId);
                return result;
            }

            var response = await _httpClient.GetAsync($"customers/{customer.Id}/subscriptions.json", cancellationToken);
            response.EnsureSuccessStatusCode();

            var subscriptionsData = await response.Content.ReadFromJsonAsync<SubscriptionsListResponse>(cancellationToken);

            if (subscriptionsData?.Subscriptions != null)
            {
                foreach (var subscription in subscriptionsData.Subscriptions)
                {
                    var detail = new SubscriptionDetailDto
                    {
                        Id = subscription.Id ?? 0,
                        CustomerId = subscription.CustomerId ?? 0,
                        State = subscription.State ?? string.Empty,
                        ActivatedAt = subscription.ActivatedAt ?? DateTime.UtcNow,
                        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                        TrialEndsAt = subscription.TrialEndsAt,
                        Price = ConvertCentsToDecimal(subscription.ProductPriceInCents ?? 0),
                        PlanHandle = subscription.ProductHandle ?? string.Empty
                    };

                    result.Subscriptions.Add(detail);
                }
            }

            _logger.LogInformation("Successfully fetched {SubscriptionCount} subscriptions for user {UserId}", result.Subscriptions.Count, userId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private async Task<long> EnsureCustomerExistsAsync(
        string userId,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Ensuring customer exists for user {UserId}", userId);

        var existingCustomer = await GetCustomerByReferenceAsync(userId, cancellationToken);
        if (existingCustomer != null)
        {
            _logger.LogInformation("Customer already exists for user {UserId}: {CustomerId}", userId, existingCustomer.Id);
            return existingCustomer.Id ?? 0;
        }

        var createRequest = new { customer = new { email, first_name = firstName, last_name = lastName, reference = userId, country = "US" } };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("customers.json", createRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var customerData = await response.Content.ReadFromJsonAsync<CustomerResponseWrapper>(cancellationToken);
            var customerId = customerData.Customer.Id ?? 0;

            _logger.LogInformation("Created new customer {CustomerId} for user {UserId}", customerId, userId);
            return customerId;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning("Customer reference {UserId} already exists in Maxio", userId);
            var customer = await GetCustomerByReferenceAsync(userId, cancellationToken);
            return customer?.Id ?? 0;
        }
    }

    private async Task<CustomerData?> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
            }

            var customerData = await response.Content.ReadFromJsonAsync<CustomerResponseWrapper>(cancellationToken);
            return customerData?.Customer;
        }
        catch (Exception ex) when (ex.Message.Contains("404"))
        {
            return null;
        }
    }

    private static decimal ConvertCentsToDecimal(long cents) => cents / 100m;
}

// API Response Models
internal class ProductsListResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("products")]
    public List<ProductData>? Products { get; set; }
}

internal class ProductData
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public long? Id { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string? Name { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("price_in_cents")]
    public long? PriceInCents { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("product_family")]
    public ProductFamilyData? ProductFamily { get; set; }
}

internal class ProductFamilyData
{
    [System.Text.Json.Serialization.JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

internal class CustomerResponseWrapper
{
    [System.Text.Json.Serialization.JsonPropertyName("customer")]
    public CustomerData? Customer { get; set; }
}

internal class CustomerData
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public long? Id { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("email")]
    public string? Email { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal class SubscriptionResponseWrapper
{
    [System.Text.Json.Serialization.JsonPropertyName("subscription")]
    public SubscriptionData? Subscription { get; set; }
}

internal class SubscriptionsListResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("subscriptions")]
    public List<SubscriptionData>? Subscriptions { get; set; }
}

internal class SubscriptionData
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public long? Id { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("customer_id")]
    public long? CustomerId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("state")]
    public string? State { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("trial_ends_at")]
    public DateTime? TrialEndsAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }
}
