using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioApiService
{
    Task<Customer?> CreateOrGetCustomerAsync(string email, string firstName, string lastName, string reference);
    Task<List<Product>> GetProductsByFamilyHandleAsync(string familyHandle);
    Task<Subscription?> CreateSubscriptionAsync(CreateSubscriptionRequest request);
    Task<Subscription?> GetSubscriptionAsync(long subscriptionId);
    Task<List<Subscription>> GetCustomerSubscriptionsAsync(long customerId);
}

public class MaxioApiService : IMaxioApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioApiService> _logger;

    public MaxioApiService(
        HttpClient httpClient,
        string apiKey,
        string baseUrl,
        string productFamilyHandle,
        ILogger<MaxioApiService> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _baseUrl = baseUrl;
        _productFamilyHandle = productFamilyHandle;
        _logger = logger;
    }

    public async Task<Customer?> CreateOrGetCustomerAsync(string email, string firstName, string lastName, string reference)
    {
        try
        {
            // Try to list customers with this email/reference to check if they exist
            var listUrl = $"{_baseUrl}/customers.json?reference={Uri.EscapeDataString(reference)}";
            var getRequest = CreateRequest("GET", listUrl);
            var getResponse = await _httpClient.SendAsync(getRequest);

            if (getResponse.IsSuccessStatusCode)
            {
                var content = await getResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var customersArray = doc.RootElement.GetProperty("customers");
                if (customersArray.GetArrayLength() > 0)
                {
                    var customerEl = customersArray[0];
                    return new Customer
                    {
                        Id = customerEl.GetProperty("id").GetInt64(),
                        Email = customerEl.GetProperty("email").GetString() ?? "",
                        FirstName = customerEl.GetProperty("first_name").GetString() ?? "",
                        LastName = customerEl.GetProperty("last_name").GetString() ?? "",
                        Reference = customerEl.GetProperty("reference").GetString()
                    };
                }
            }

            // Customer doesn't exist, create one
            var createUrl = $"{_baseUrl}/customers.json";
            var createPayload = new
            {
                customer = new
                {
                    email,
                    first_name = firstName,
                    last_name = lastName,
                    reference
                }
            };

            var postRequest = CreateRequest("POST", createUrl);
            var jsonContent = JsonSerializer.Serialize(createPayload);
            postRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var postResponse = await _httpClient.SendAsync(postRequest);
            if (postResponse.IsSuccessStatusCode)
            {
                var responseContent = await postResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseContent);
                var customerEl = doc.RootElement.GetProperty("customer");
                return new Customer
                {
                    Id = customerEl.GetProperty("id").GetInt64(),
                    Email = customerEl.GetProperty("email").GetString() ?? "",
                    FirstName = customerEl.GetProperty("first_name").GetString() ?? "",
                    LastName = customerEl.GetProperty("last_name").GetString() ?? "",
                    Reference = customerEl.GetProperty("reference").GetString()
                };
            }

            var errorContent = await postResponse.Content.ReadAsStringAsync();
            _logger.LogError($"Failed to create Maxio customer: {postResponse.StatusCode} - {errorContent}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/getting Maxio customer");
            return null;
        }
    }

    public async Task<List<Product>> GetProductsByFamilyHandleAsync(string familyHandle)
    {
        var products = new List<Product>();
        try
        {
            var url = $"{_baseUrl}/product_families/handle/{Uri.EscapeDataString(familyHandle)}/products.json";
            var request = CreateRequest("GET", url);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var productsArray = doc.RootElement.GetProperty("products");

                foreach (var productEl in productsArray.EnumerateArray())
                {
                    products.Add(new Product
                    {
                        Id = productEl.GetProperty("id").GetInt64(),
                        Name = productEl.GetProperty("name").GetString() ?? "",
                        Handle = productEl.GetProperty("handle").GetString() ?? "",
                        Description = productEl.GetProperty("description").GetString() ?? "",
                        PriceInCents = productEl.GetProperty("price_in_cents").GetInt64(),
                        Interval = productEl.GetProperty("interval").GetInt32(),
                        IntervalUnit = productEl.GetProperty("interval_unit").GetString() ?? "month"
                    });
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to get products: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products from Maxio");
        }

        return products;
    }

    public async Task<Subscription?> CreateSubscriptionAsync(CreateSubscriptionRequest request)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions.json";
            var httpRequest = CreateRequest("POST", url);

            var payload = new { subscription = request };
            var jsonContent = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpRequest);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseContent);
                var subEl = doc.RootElement.GetProperty("subscription");
                return ParseSubscription(subEl);
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"Failed to create subscription: {response.StatusCode} - {errorContent}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            return null;
        }
    }

    public async Task<Subscription?> GetSubscriptionAsync(long subscriptionId)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions/{subscriptionId}.json";
            var request = CreateRequest("GET", url);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var subEl = doc.RootElement.GetProperty("subscription");
                return ParseSubscription(subEl);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting subscription");
            return null;
        }
    }

    public async Task<List<Subscription>> GetCustomerSubscriptionsAsync(long customerId)
    {
        var subscriptions = new List<Subscription>();
        try
        {
            var url = $"{_baseUrl}/customers/{customerId}/subscriptions.json";
            var request = CreateRequest("GET", url);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var subsArray = doc.RootElement.GetProperty("subscriptions");

                foreach (var subEl in subsArray.EnumerateArray())
                {
                    subscriptions.Add(ParseSubscription(subEl));
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to get customer subscriptions: {response.StatusCode} - {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer subscriptions");
        }

        return subscriptions;
    }

    private HttpRequestMessage CreateRequest(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private Subscription ParseSubscription(JsonElement subEl)
    {
        var sub = new Subscription
        {
            Id = subEl.GetProperty("id").GetInt64(),
            State = subEl.GetProperty("state").GetString() ?? "",
            BalanceInCents = subEl.GetProperty("balance_in_cents").GetInt64(),
            ProductPriceInCents = subEl.GetProperty("product_price_in_cents").GetInt64(),
            CurrentPeriodEndsAt = subEl.TryGetProperty("current_period_ends_at", out var ceEl) && ceEl.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(ceEl.GetString() ?? "")
                : null,
            NextAssessmentAt = subEl.TryGetProperty("next_assessment_at", out var naEl) && naEl.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(naEl.GetString() ?? "")
                : null,
            ActivatedAt = subEl.TryGetProperty("activated_at", out var aaEl) && aaEl.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(aaEl.GetString() ?? "")
                : null,
            CanceledAt = subEl.TryGetProperty("canceled_at", out var caEl) && caEl.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(caEl.GetString() ?? "")
                : null,
        };

        if (subEl.TryGetProperty("customer", out var customerEl) && customerEl.ValueKind != JsonValueKind.Null)
        {
            sub.Customer = new Customer
            {
                Id = customerEl.GetProperty("id").GetInt64(),
                Email = customerEl.GetProperty("email").GetString() ?? "",
                FirstName = customerEl.GetProperty("first_name").GetString() ?? "",
                LastName = customerEl.GetProperty("last_name").GetString() ?? "",
            };
        }

        if (subEl.TryGetProperty("product", out var productEl) && productEl.ValueKind != JsonValueKind.Null)
        {
            sub.Product = new Product
            {
                Id = productEl.GetProperty("id").GetInt64(),
                Name = productEl.GetProperty("name").GetString() ?? "",
                Handle = productEl.GetProperty("handle").GetString() ?? "",
                PriceInCents = productEl.GetProperty("price_in_cents").GetInt64(),
                Interval = productEl.GetProperty("interval").GetInt32(),
                IntervalUnit = productEl.GetProperty("interval_unit").GetString() ?? "month"
            };
        }

        return sub;
    }
}

public class Customer
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Reference { get; set; }
}

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string Description { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class Subscription
{
    public long Id { get; set; }
    public string State { get; set; } = "";
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public Customer? Customer { get; set; }
    public Product? Product { get; set; }
}

public class CreateSubscriptionRequest
{
    public long? customer_id { get; set; }
    public string? customer_reference { get; set; }
    public CustomerAttributes? customer_attributes { get; set; }
    public string? product_handle { get; set; }
    public long? product_id { get; set; }
    public string? payment_collection_method { get; set; }
    public bool? skip_billing_manifest_taxes { get; set; }
}

public class CustomerAttributes
{
    public string? first_name { get; set; }
    public string? last_name { get; set; }
    public string? email { get; set; }
    public string? reference { get; set; }
}
