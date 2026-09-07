using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService>? _logger;

    public MaxioService(HttpClient httpClient, MaxioSettings settings, ILogger<MaxioService>? logger = null)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        var baseUrl = settings.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x")));
    }

    public async Task<IEnumerable<MaxioProductDto>> GetProductsForFamilyAsync(string familyHandle)
    {
        try
        {
            var url = $"/product_families/handle:{familyHandle}/products.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var products = new List<MaxioProductDto>();

            if (root.TryGetProperty("items", out var itemsArray))
            {
                foreach (var item in itemsArray.EnumerateArray())
                {
                    if (item.TryGetProperty("product", out var productObj))
                    {
                        products.Add(ParseProduct(productObj));
                    }
                }
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error fetching products for family {Handle}", familyHandle);
            throw;
        }
    }

    public async Task<MaxioCustomerDto?> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        // First, try to look up the customer by reference
        var customer = await LookupCustomerByReferenceAsync(userId);
        if (customer != null)
            return customer;

        // If not found, create a new customer
        return await CreateCustomerAsync(userId, email, firstName, lastName);
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(long customerId, string productHandle)
    {
        try
        {
            var requestBody = new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_id = customerId,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("subscription", out var subscriptionObj))
            {
                return ParseSubscription(subscriptionObj);
            }

            throw new InvalidOperationException("Invalid response from Maxio API");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error creating subscription for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<IEnumerable<MaxioSubscriptionDto>> GetCustomerSubscriptionsAsync(long customerId)
    {
        try
        {
            var url = $"/customers/{customerId}/subscriptions.json";
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new List<MaxioSubscriptionDto>();

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var subscriptions = new List<MaxioSubscriptionDto>();

            if (root.TryGetProperty("subscriptions", out var subscriptionsArray))
            {
                foreach (var sub in subscriptionsArray.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscription(sub));
                }
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error fetching subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    private async Task<MaxioCustomerDto?> LookupCustomerByReferenceAsync(string reference)
    {
        try
        {
            var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerObj))
            {
                return ParseCustomer(customerObj);
            }

            return null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error looking up customer by reference {Reference}", reference);
            throw;
        }
    }

    private async Task<MaxioCustomerDto> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        try
        {
            var requestBody = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = reference
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/customers.json", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (root.TryGetProperty("customer", out var customerObj))
            {
                return ParseCustomer(customerObj);
            }

            throw new InvalidOperationException("Invalid response from Maxio API");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error creating customer {Reference}", reference);
            throw;
        }
    }

    private static MaxioCustomerDto ParseCustomer(JsonElement obj)
    {
        return new MaxioCustomerDto
        {
            Id = obj.GetProperty("id").GetInt64(),
            Email = obj.GetProperty("email").GetString() ?? "",
            FirstName = obj.GetProperty("first_name").GetString() ?? "",
            LastName = obj.GetProperty("last_name").GetString() ?? "",
            Reference = obj.TryGetProperty("reference", out var refObj) ? refObj.GetString() : null
        };
    }

    private static MaxioProductDto ParseProduct(JsonElement obj)
    {
        return new MaxioProductDto
        {
            Id = obj.GetProperty("id").GetInt64(),
            Handle = obj.TryGetProperty("handle", out var h) ? h.GetString() ?? "" : "",
            Name = obj.GetProperty("name").GetString() ?? "",
            Description = obj.TryGetProperty("description", out var d) ? d.GetString() : null,
            PriceInCents = obj.GetProperty("price_in_cents").GetInt64(),
            Interval = obj.GetProperty("interval").GetInt32(),
            IntervalUnit = obj.GetProperty("interval_unit").GetString() ?? "month"
        };
    }

    private static MaxioSubscriptionDto ParseSubscription(JsonElement obj)
    {
        return new MaxioSubscriptionDto
        {
            Id = obj.GetProperty("id").GetInt64(),
            CustomerId = obj.GetProperty("customer_id").GetInt64(),
            ProductId = obj.TryGetProperty("product_id", out var pid) ? pid.GetInt64() : 0,
            ProductHandle = obj.TryGetProperty("product_handle", out var ph) ? ph.GetString() : null,
            State = obj.GetProperty("state").GetString() ?? "",
            CurrentPeriodStartsAt = GetOptionalDateTime(obj, "current_period_starts_at"),
            CurrentPeriodEndsAt = GetOptionalDateTime(obj, "current_period_ends_at"),
            NextBillingAt = GetOptionalDateTime(obj, "next_billing_at")
        };
    }

    private static DateTime? GetOptionalDateTime(JsonElement obj, string property)
    {
        if (obj.TryGetProperty(property, out var element) && element.ValueKind != JsonValueKind.Null)
        {
            if (element.TryGetDateTime(out var dt))
                return dt;
        }
        return null;
    }
}
