using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioBillingService
{
    Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync();
    Task<MaxioSubscription> CreateSubscriptionAsync(string eShopUserId, string firstName, string lastName, string email, string productHandle);
    Task<MaxioSubscription[]> GetUserSubscriptionsAsync(string eShopUserId);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly string _baseUrl;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _baseUrl = !string.IsNullOrEmpty(_settings.BaseUrl)
            ? _settings.BaseUrl.TrimEnd('/')
            : $"https://{_settings.Subdomain}.chargify.com";

        SetupAuthentication();
    }

    private void SetupAuthentication()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync()
    {
        try
        {
            var url = $"{_baseUrl}/product_families/handle:{_settings.ProductFamilyHandle}/products.json";
            _logger.LogDebug("Fetching subscription plans from {Url}", url);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("items", out var itemsElement))
            {
                _logger.LogWarning("No 'items' property found in Maxio response");
                return Array.Empty<MaxioSubscriptionPlan>();
            }

            var plans = new List<MaxioSubscriptionPlan>();
            foreach (var item in itemsElement.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var productElement))
                {
                    var plan = ParseProduct(productElement);
                    plans.Add(plan);
                }
            }

            return plans.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string eShopUserId, string firstName, string lastName, string email, string productHandle)
    {
        try
        {
            var customerReference = $"eshop-{eShopUserId}";

            var existingCustomer = await GetOrCreateCustomerAsync(customerReference, firstName, lastName, email);

            var subscriptionPayload = new
            {
                subscription = new
                {
                    customer_id = existingCustomer.Id,
                    product_handle = productHandle
                }
            };

            var url = $"{_baseUrl}/subscriptions.json";
            var jsonContent = JsonSerializer.Serialize(subscriptionPayload);
            _logger.LogDebug("Creating subscription with payload: {Payload}", jsonContent);

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscriptionElement))
            {
                return ParseSubscription(subscriptionElement);
            }

            throw new InvalidOperationException("Invalid response structure from Maxio");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription[]> GetUserSubscriptionsAsync(string eShopUserId)
    {
        try
        {
            var customerReference = $"eshop-{eShopUserId}";

            var customer = await TryGetCustomerByReferenceAsync(customerReference);
            if (customer == null)
            {
                return Array.Empty<MaxioSubscription>();
            }

            var url = $"{_baseUrl}/customers/{customer.Id}/subscriptions.json";
            _logger.LogDebug("Fetching subscriptions for customer {CustomerId}", customer.Id);

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch subscriptions: {StatusCode}", response.StatusCode);
                return Array.Empty<MaxioSubscription>();
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("subscriptions", out var subscriptionsElement))
            {
                return Array.Empty<MaxioSubscription>();
            }

            var subscriptions = new List<MaxioSubscription>();
            foreach (var item in subscriptionsElement.EnumerateArray())
            {
                var subscription = ParseSubscription(item);
                subscriptions.Add(subscription);
            }

            return subscriptions.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", eShopUserId);
            throw;
        }
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var existing = await TryGetCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            return existing;
        }

        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference
            }
        };

        var url = $"{_baseUrl}/customers.json";
        var jsonContent = JsonSerializer.Serialize(payload);
        _logger.LogDebug("Creating customer {Reference}", reference);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;

        if (root.TryGetProperty("customer", out var customerElement))
        {
            return ParseCustomer(customerElement);
        }

        throw new InvalidOperationException("Invalid response structure from Maxio");
    }

    private async Task<MaxioCustomer?> TryGetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"{_baseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerElement))
            {
                return ParseCustomer(customerElement);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error looking up customer by reference");
            return null;
        }
    }

    private MaxioCustomer ParseCustomer(JsonElement element)
    {
        var id = element.GetProperty("id").GetInt32();
        var firstName = element.GetProperty("first_name").GetString() ?? string.Empty;
        var lastName = element.GetProperty("last_name").GetString() ?? string.Empty;
        var email = element.GetProperty("email").GetString() ?? string.Empty;

        return new MaxioCustomer
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Email = email
        };
    }

    private MaxioSubscriptionPlan ParseProduct(JsonElement element)
    {
        var id = element.GetProperty("id").GetInt32();
        var name = element.GetProperty("name").GetString() ?? string.Empty;
        var handle = element.GetProperty("handle").GetString();
        var description = element.GetProperty("description").GetString();
        var priceInCents = element.GetProperty("price_in_cents").GetInt64();
        var interval = element.GetProperty("interval").GetInt32();
        var intervalUnit = element.GetProperty("interval_unit").GetString() ?? "month";

        return new MaxioSubscriptionPlan
        {
            Id = id,
            Name = name,
            Handle = handle,
            Description = description,
            PriceInCents = priceInCents,
            Interval = interval,
            IntervalUnit = intervalUnit
        };
    }

    private MaxioSubscription ParseSubscription(JsonElement element)
    {
        var id = element.GetProperty("id").GetInt64();
        var customerId = element.GetProperty("customer_id").GetInt32();
        var state = element.GetProperty("state").GetString() ?? "unknown";

        var productElement = element.GetProperty("product");
        var productName = productElement.GetProperty("name").GetString() ?? string.Empty;
        var productHandle = productElement.GetProperty("handle").GetString();

        var nextBillingAtString = element.GetProperty("next_billing_at").GetString();
        DateTime? nextBillingAt = DateTime.TryParse(nextBillingAtString, out var parsedBilling) ? parsedBilling : null;

        var currentPeriodEndsAtString = element.GetProperty("current_period_ends_at").GetString();
        DateTime? currentPeriodEndsAt = DateTime.TryParse(currentPeriodEndsAtString, out var parsedPeriod) ? parsedPeriod : null;

        var createdAtString = element.GetProperty("created_at").GetString();
        var createdAt = DateTime.TryParse(createdAtString, out var parsed3) ? parsed3 : DateTime.UtcNow;

        return new MaxioSubscription
        {
            Id = id,
            CustomerId = customerId,
            State = state,
            ProductName = productName,
            ProductHandle = productHandle,
            NextBillingAt = nextBillingAt,
            CurrentPeriodEndsAt = currentPeriodEndsAt,
            CreatedAt = createdAt
        };
    }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class MaxioSubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";

    public decimal GetPrice() => PriceInCents / 100m;
}

public class MaxioSubscription
{
    public long Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
