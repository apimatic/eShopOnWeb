using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioService
{
    Task<MaxioSubscription> GetOrCreateCustomerAndSubscribeAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle);

    Task<List<MaxioProduct>> GetProductsAsync();
    Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(string customerReference);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _subdomain;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var maxioConfig = configuration.GetSection("Maxio");
        _apiKey = maxioConfig["ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey is required");
        _subdomain = maxioConfig["Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain is required");
        _productFamilyHandle = maxioConfig["ProductFamilyHandle"] ?? throw new InvalidOperationException("Maxio:ProductFamilyHandle is required");

        var baseUrl = maxioConfig["BaseUrl"];
        if (!string.IsNullOrEmpty(baseUrl))
        {
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
        else
        {
            _httpClient.BaseAddress = new Uri($"https://{_subdomain}.chargify.com");
        }

        SetAuthHeader();
    }

    private void SetAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_apiKey}:X"));
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<MaxioSubscription> GetOrCreateCustomerAndSubscribeAsync(
        string userId,
        string firstName,
        string lastName,
        string email,
        string productHandle)
    {
        try
        {
            var customerReference = $"eshop-{userId}";
            var customer = await GetOrCreateCustomerAsync(customerReference, firstName, lastName, email);

            var subscription = await CreateSubscriptionAsync(customer.Id, productHandle, customerReference);
            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId}", userId);
            throw;
        }
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email)
    {
        try
        {
            var existingCustomer = await GetCustomerByReferenceAsync(reference);
            if (existingCustomer != null)
            {
                return existingCustomer;
            }
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
        }

        return await CreateCustomerAsync(reference, firstName, lastName, email);
    }

    private async Task<MaxioCustomer> GetCustomerByReferenceAsync(string reference)
    {
        var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("customer", out var customerEl))
        {
            return new MaxioCustomer
            {
                Id = customerEl.GetProperty("id").GetInt32(),
                FirstName = customerEl.GetProperty("first_name").GetString(),
                LastName = customerEl.GetProperty("last_name").GetString(),
                Email = customerEl.GetProperty("email").GetString(),
                Reference = customerEl.GetProperty("reference").GetString(),
            };
        }

        return null;
    }

    private async Task<MaxioCustomer> CreateCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email)
    {
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = reference,
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var customerEl = doc.RootElement.GetProperty("customer");

        return new MaxioCustomer
        {
            Id = customerEl.GetProperty("id").GetInt32(),
            FirstName = customerEl.GetProperty("first_name").GetString(),
            LastName = customerEl.GetProperty("last_name").GetString(),
            Email = customerEl.GetProperty("email").GetString(),
            Reference = customerEl.GetProperty("reference").GetString(),
        };
    }

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference)
    {
        var payload = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "invoice",
                reference = reference,
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/subscriptions.json", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var subEl = doc.RootElement.GetProperty("subscription");

        return ParseSubscription(subEl);
    }

    public async Task<List<MaxioProduct>> GetProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/product_families/handle:{Uri.EscapeDataString(_productFamilyHandle)}/products.json?per_page=200");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.GetProperty("items");

            var products = new List<MaxioProduct>();
            foreach (var item in items.EnumerateArray())
            {
                var productEl = item.GetProperty("product");
                products.Add(new MaxioProduct
                {
                    Id = productEl.GetProperty("id").GetInt32(),
                    Name = productEl.GetProperty("name").GetString(),
                    Handle = productEl.GetProperty("handle").GetString(),
                    Description = productEl.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    PriceInCents = productEl.GetProperty("price_in_cents").GetInt64(),
                    Interval = productEl.GetProperty("interval").GetInt32(),
                    IntervalUnit = productEl.GetProperty("interval_unit").GetString(),
                });
            }

            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products from Maxio");
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> GetCustomerSubscriptionsAsync(string customerReference)
    {
        try
        {
            var customer = await GetCustomerByReferenceAsync(customerReference);
            if (customer == null)
            {
                return new List<MaxioSubscription>();
            }

            var response = await _httpClient.GetAsync($"/customers/{customer.Id}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var subscriptionsEl = doc.RootElement.GetProperty("subscriptions");

            var subscriptions = new List<MaxioSubscription>();
            foreach (var subEl in subscriptionsEl.EnumerateArray())
            {
                subscriptions.Add(ParseSubscription(subEl));
            }

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for reference {Reference}", customerReference);
            throw;
        }
    }

    private static MaxioSubscription ParseSubscription(JsonElement subEl)
    {
        var productEl = subEl.TryGetProperty("product", out var prod) ? prod : default;
        return new MaxioSubscription
        {
            Id = subEl.GetProperty("id").GetInt32(),
            State = subEl.GetProperty("state").GetString(),
            CustomerId = subEl.GetProperty("customer_id").GetInt32(),
            ProductHandle = productEl.ValueKind != JsonValueKind.Null && productEl.TryGetProperty("handle", out var handle)
                ? handle.GetString()
                : null,
            PriceInCents = subEl.GetProperty("price_in_cents").GetInt64(),
            NextBillingAt = subEl.TryGetProperty("next_billing_at", out var nba) && nba.ValueKind != JsonValueKind.Null
                ? DateTime.Parse(nba.GetString())
                : null,
            CreatedAt = DateTime.Parse(subEl.GetProperty("created_at").GetString()),
            UpdatedAt = DateTime.Parse(subEl.GetProperty("updated_at").GetString()),
        };
    }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string Reference { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; }
    public string ProductHandle { get; set; }
    public long PriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Handle { get; set; }
    public string Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
}
