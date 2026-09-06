using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioApiClient
{
    Task<MaxioProduct[]> ListProductsAsync(string productFamilyHandle);
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string customerReference, string email, string firstName, string lastName);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId);
    Task<MaxioSubscription[]> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        var baseUrl = settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = $"{settings.ApiKey}:X";
        var encodedCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(credentials));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {encodedCredentials}");
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
    }

    public async Task<MaxioProduct[]> ListProductsAsync(string productFamilyHandle)
    {
        try
        {
            var url = $"/product_families/handle:{productFamilyHandle}/products.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var products = new List<MaxioProduct>();

            if (doc.RootElement.TryGetProperty("products", out var productsArray))
            {
                foreach (var item in productsArray.EnumerateArray())
                {
                    products.Add(new MaxioProduct
                    {
                        Id = item.GetProperty("id").GetInt32(),
                        Name = item.GetProperty("name").GetString() ?? "",
                        Handle = item.GetProperty("handle").GetString(),
                        PriceInCents = item.GetProperty("price_in_cents").GetInt64(),
                        Description = item.GetProperty("description").GetString()
                    });
                }
            }

            return products.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products from Maxio");
            throw;
        }
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string customerReference, string email, string firstName, string lastName)
    {
        try
        {
            var customer = await GetCustomerByReferenceAsync(customerReference);
            if (customer != null)
            {
                return customer;
            }

            return await CreateCustomerAsync(customerReference, email, firstName, lastName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting or creating customer from Maxio");
            throw;
        }
    }

    private async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string customerReference)
    {
        try
        {
            var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}";
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("customer", out var customerElement))
            {
                return ParseCustomerFromJson(customerElement);
            }

            return null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer from Maxio");
            throw;
        }
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(string customerReference, string email, string firstName, string lastName)
    {
        try
        {
            var payload = new
            {
                customer = new
                {
                    reference = customerReference,
                    email = email,
                    first_name = firstName,
                    last_name = lastName
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/customers.json", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("customer", out var customerElement))
            {
                return ParseCustomerFromJson(customerElement);
            }

            throw new InvalidOperationException("Invalid response from Maxio when creating customer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer in Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("subscription", out var subscriptionElement))
            {
                return ParseSubscriptionFromJson(subscriptionElement);
            }

            throw new InvalidOperationException("Invalid response from Maxio when creating subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var url = $"/subscriptions/{subscriptionId}.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("subscription", out var subscriptionElement))
            {
                return ParseSubscriptionFromJson(subscriptionElement);
            }

            throw new InvalidOperationException("Invalid response from Maxio when getting subscription");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription from Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription[]> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"/customers/{customerId}/subscriptions.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var subscriptions = new List<MaxioSubscription>();

            if (doc.RootElement.TryGetProperty("subscriptions", out var subscriptionsArray))
            {
                foreach (var item in subscriptionsArray.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscriptionFromJson(item));
                }
            }

            return subscriptions.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing customer subscriptions from Maxio");
            throw;
        }
    }

    private static MaxioCustomer ParseCustomerFromJson(JsonElement element)
    {
        return new MaxioCustomer
        {
            Id = element.GetProperty("id").GetInt32(),
            Reference = element.TryGetProperty("reference", out var refElement) ? refElement.GetString() : null,
            Email = element.GetProperty("email").GetString() ?? "",
            FirstName = element.GetProperty("first_name").GetString() ?? "",
            LastName = element.GetProperty("last_name").GetString() ?? ""
        };
    }

    private static MaxioSubscription ParseSubscriptionFromJson(JsonElement element)
    {
        var state = element.GetProperty("state").GetString() ?? "unknown";
        var startDate = DateTime.Parse(element.GetProperty("created_at").GetString()!);
        var nextBillingAt = element.TryGetProperty("next_billing_at", out var nextBillingElement) && nextBillingElement.ValueKind != JsonValueKind.Null
            ? DateTime.Parse(nextBillingElement.GetString()!)
            : startDate.AddMonths(1);

        var product = element.GetProperty("product");
        var productHandle = product.GetProperty("handle").GetString() ?? "";
        var productName = product.GetProperty("name").GetString() ?? "";

        var priceInCents = element.GetProperty("current_period_ends_at").ValueKind != JsonValueKind.Null
            ? element.TryGetProperty("total_revenue_in_cents", out var totalRevenueElement)
                ? totalRevenueElement.GetInt64()
                : 0
            : product.TryGetProperty("price_in_cents", out var priceElement)
                ? priceElement.GetInt64()
                : 0;

        return new MaxioSubscription
        {
            Id = element.GetProperty("id").GetInt32(),
            CustomerId = element.GetProperty("customer_id").GetInt32(),
            ProductHandle = productHandle,
            ProductName = productName,
            PriceInCents = priceInCents,
            State = state,
            CreatedAt = startDate,
            NextBillingAt = nextBillingAt
        };
    }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Handle { get; set; }
    public long PriceInCents { get; set; }
    public string? Description { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string Email { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public long PriceInCents { get; set; }
    public string State { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime NextBillingAt { get; set; }
}
