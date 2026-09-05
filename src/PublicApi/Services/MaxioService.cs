using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<SubscriptionPlanDto[]> ListPlansAsync();
    Task<CustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<SubscriptionDto[]> ListSubscriptionsForCustomerAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioService> _logger;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioService(IOptions<MaxioOptions> options, ILogger<MaxioService> logger, HttpClient? httpClient = null)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    private string GetAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiKey}:x"));
        return $"Basic {credentials}";
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, string? body = null)
    {
        var request = new HttpRequestMessage(method, $"{_options.GetBaseUrl()}{endpoint}")
        {
            Headers =
            {
                { "Authorization", GetAuthHeader() },
                { "Accept", "application/json" }
            }
        };

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    public async Task<SubscriptionPlanDto[]> ListPlansAsync()
    {
        try
        {
            _logger.LogInformation("Fetching subscription plans from Maxio");
            var request = CreateRequest(HttpMethod.Get, "/products.json");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var plans = new List<SubscriptionPlanDto>();

            if (root.TryGetProperty("products", out var productsElement))
            {
                foreach (var productWrapper in productsElement.EnumerateArray())
                {
                    if (productWrapper.TryGetProperty("product", out var productElement))
                    {
                        var product = JsonSerializer.Deserialize<MaxioProduct>(productElement.GetRawText(), _jsonOptions);
                        if (product?.ProductFamily?.Handle == _options.ProductFamilyHandle)
                        {
                            plans.Add(new SubscriptionPlanDto
                            {
                                Id = product.Id ?? 0,
                                Handle = product.Handle ?? string.Empty,
                                Name = product.Name ?? string.Empty,
                                Description = product.Description ?? string.Empty,
                                PriceInCents = product.PriceInCents ?? 0,
                                IntervalUnit = product.IntervalUnit ?? "month"
                            });
                        }
                    }
                }
            }

            _logger.LogInformation("Found {Count} plans", plans.Count);
            return plans.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching plans from Maxio");
            throw;
        }
    }

    public async Task<CustomerDto> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            _logger.LogInformation("Looking up customer by reference: {UserId}", userId);

            var lookupRequest = CreateRequest(HttpMethod.Get, $"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}");
            var lookupResponse = await _httpClient.SendAsync(lookupRequest);

            if (lookupResponse.IsSuccessStatusCode)
            {
                var content = await lookupResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.TryGetProperty("customer", out var customerElement))
                {
                    var customer = JsonSerializer.Deserialize<MaxioCustomer>(customerElement.GetRawText(), _jsonOptions);
                    if (customer != null)
                    {
                        _logger.LogInformation("Found existing customer: {CustomerId}", customer.Id);
                        return new CustomerDto
                        {
                            Id = customer.Id ?? 0,
                            Email = customer.Email ?? string.Empty,
                            FirstName = customer.FirstName ?? string.Empty,
                            LastName = customer.LastName ?? string.Empty,
                            Reference = customer.Reference ?? string.Empty
                        };
                    }
                }
            }

            _logger.LogInformation("Customer not found, creating new customer");
            var createPayload = new { customer = new { first_name = firstName, last_name = lastName, email = email, reference = userId } };
            var createBody = JsonSerializer.Serialize(createPayload);
            var createRequest = CreateRequest(HttpMethod.Post, "/customers.json", createBody);

            var createResponse = await _httpClient.SendAsync(createRequest);
            createResponse.EnsureSuccessStatusCode();

            var createContent = await createResponse.Content.ReadAsStringAsync();
            using var createDoc = JsonDocument.Parse(createContent);
            var createRoot = createDoc.RootElement;

            if (createRoot.TryGetProperty("customer", out var newCustomerElement))
            {
                var newCustomer = JsonSerializer.Deserialize<MaxioCustomer>(newCustomerElement.GetRawText(), _jsonOptions);
                _logger.LogInformation("Created new customer: {CustomerId}", newCustomer?.Id);
                return new CustomerDto
                {
                    Id = newCustomer?.Id ?? 0,
                    Email = newCustomer?.Email ?? string.Empty,
                    FirstName = newCustomer?.FirstName ?? string.Empty,
                    LastName = newCustomer?.LastName ?? string.Empty,
                    Reference = newCustomer?.Reference ?? string.Empty
                };
            }

            throw new InvalidOperationException("Failed to create customer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            _logger.LogInformation("Creating subscription for customer {CustomerId} with product {ProductHandle}", customerId, productHandle);

            var createPayload = new { subscription = new { customer_id = customerId, product_handle = productHandle } };
            var createBody = JsonSerializer.Serialize(createPayload);
            var createRequest = CreateRequest(HttpMethod.Post, "/subscriptions.json", createBody);

            var createResponse = await _httpClient.SendAsync(createRequest);
            createResponse.EnsureSuccessStatusCode();

            var createContent = await createResponse.Content.ReadAsStringAsync();
            using var createDoc = JsonDocument.Parse(createContent);
            var createRoot = createDoc.RootElement;

            if (createRoot.TryGetProperty("subscription", out var subscriptionElement))
            {
                var subscription = JsonSerializer.Deserialize<MaxioSubscription>(subscriptionElement.GetRawText(), _jsonOptions);
                _logger.LogInformation("Created subscription: {SubscriptionId}", subscription?.Id);

                return new SubscriptionDto
                {
                    Id = subscription?.Id ?? 0,
                    State = subscription?.State ?? "unknown",
                    CustomerId = subscription?.CustomerId ?? 0,
                    ProductId = subscription?.ProductId ?? 0,
                    ProductName = subscription?.Product?.Name ?? string.Empty,
                    CreatedAt = subscription?.CreatedAt ?? DateTime.UtcNow,
                    NextBillingAt = subscription?.NextBillingAt
                };
            }

            throw new InvalidOperationException("Failed to create subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            throw;
        }
    }

    public async Task<SubscriptionDto[]> ListSubscriptionsForCustomerAsync(int customerId)
    {
        try
        {
            _logger.LogInformation("Fetching subscriptions for customer {CustomerId}", customerId);

            var request = CreateRequest(HttpMethod.Get, $"/subscriptions.json?customer_id={customerId}");
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var subscriptions = new List<SubscriptionDto>();

            if (root.TryGetProperty("subscriptions", out var subscriptionsElement))
            {
                foreach (var subscriptionWrapper in subscriptionsElement.EnumerateArray())
                {
                    if (subscriptionWrapper.TryGetProperty("subscription", out var subscriptionElement))
                    {
                        var subscription = JsonSerializer.Deserialize<MaxioSubscription>(subscriptionElement.GetRawText(), _jsonOptions);
                        if (subscription != null)
                        {
                            subscriptions.Add(new SubscriptionDto
                            {
                                Id = subscription.Id ?? 0,
                                State = subscription.State ?? "unknown",
                                CustomerId = subscription.CustomerId ?? 0,
                                ProductId = subscription.ProductId ?? 0,
                                ProductName = subscription.Product?.Name ?? string.Empty,
                                CreatedAt = subscription.CreatedAt ?? DateTime.UtcNow,
                                NextBillingAt = subscription.NextBillingAt
                            });
                        }
                    }
                }
            }

            _logger.LogInformation("Found {Count} subscriptions for customer {CustomerId}", subscriptions.Count, customerId);
            return subscriptions.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions");
            throw;
        }
    }
}

internal class MaxioProduct
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int? PriceInCents { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; set; }
}

internal class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("next_billing_at")]
    public DateTime? NextBillingAt { get; set; }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = "month";

    public decimal GetPrice() => PriceInCents / 100m;
}

public class CustomerDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
}
