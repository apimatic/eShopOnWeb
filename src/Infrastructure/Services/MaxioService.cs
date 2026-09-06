using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}

public interface IMaxioService
{
    Task<(bool Success, List<MaxioProduct> Products)> ListProductsAsync();
    Task<(bool Success, MaxioCustomer? Customer)> CreateCustomerAsync(string externalId, string firstName, string lastName, string email);
    Task<(bool Success, MaxioCustomer? Customer)> GetOrCreateCustomerAsync(string externalId, string firstName, string lastName, string email);
    Task<(bool Success, MaxioSubscription? Subscription)> CreateSubscriptionAsync(int customerId, int productId, DateTime? startDate = null);
    Task<(bool Success, List<MaxioSubscription> Subscriptions)> ListCustomerSubscriptionsAsync(int customerId);
    Task<(bool Success, MaxioCustomer? Customer)> GetCustomerByExternalIdAsync(string externalId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = _options.BaseUrl ?? $"https://{_options.Subdomain}.chargify.com";
        _httpClient.BaseAddress = new Uri(baseUrl);
        SetBasicAuth();
    }

    private void SetBasicAuth()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<(bool Success, List<MaxioProduct> Products)> ListProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/products.json");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to list products: {StatusCode}", response.StatusCode);
                return (false, new List<MaxioProduct>());
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var products = new List<MaxioProduct>();

            if (jsonDoc.RootElement.TryGetProperty("products", out var productsArray))
            {
                foreach (var product in productsArray.EnumerateArray())
                {
                    var handle = product.GetProperty("handle").GetString() ?? string.Empty;
                    if (handle == _options.ProductFamilyHandle || handle.StartsWith(_options.ProductFamilyHandle + "-"))
                    {
                        products.Add(new MaxioProduct
                        {
                            Id = product.GetProperty("id").GetInt32(),
                            Handle = handle,
                            Name = product.GetProperty("name").GetString() ?? string.Empty,
                            ProductFamilyId = product.GetProperty("product_family").GetProperty("id").GetInt32(),
                        });
                    }
                }
            }

            return (true, products);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while listing products");
            return (false, new List<MaxioProduct>());
        }
    }

    public async Task<(bool Success, MaxioCustomer? Customer)> CreateCustomerAsync(string externalId, string firstName, string lastName, string email)
    {
        try
        {
            var payload = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = externalId
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/customers.json", content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create customer: {StatusCode} - {Content}", response.StatusCode, await response.Content.ReadAsStringAsync());
                return (false, null);
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);
            var customer = ParseMaxioCustomer(jsonDoc.RootElement.GetProperty("customer"));

            return (true, customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while creating customer");
            return (false, null);
        }
    }

    public async Task<(bool Success, MaxioCustomer? Customer)> GetOrCreateCustomerAsync(string externalId, string firstName, string lastName, string email)
    {
        var existing = await GetCustomerByExternalIdAsync(externalId);
        if (existing.Success && existing.Customer != null)
        {
            return (true, existing.Customer);
        }

        return await CreateCustomerAsync(externalId, firstName, lastName, email);
    }

    public async Task<(bool Success, MaxioSubscription? Subscription)> CreateSubscriptionAsync(int customerId, int productId, DateTime? startDate = null)
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_id = productId,
                    activated_at = startDate ?? DateTime.UtcNow
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to create subscription: {StatusCode} - {Content}", response.StatusCode, await response.Content.ReadAsStringAsync());
                return (false, null);
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);
            var subscription = ParseMaxioSubscription(jsonDoc.RootElement.GetProperty("subscription"));

            return (true, subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while creating subscription");
            return (false, null);
        }
    }

    public async Task<(bool Success, List<MaxioSubscription> Subscriptions)> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to list customer subscriptions: {StatusCode}", response.StatusCode);
                return (false, new List<MaxioSubscription>());
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var subscriptions = new List<MaxioSubscription>();

            if (jsonDoc.RootElement.TryGetProperty("subscriptions", out var subsArray))
            {
                foreach (var sub in subsArray.EnumerateArray())
                {
                    subscriptions.Add(ParseMaxioSubscription(sub));
                }
            }

            return (true, subscriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while listing customer subscriptions");
            return (false, new List<MaxioSubscription>());
        }
    }

    public async Task<(bool Success, MaxioCustomer? Customer)> GetCustomerByExternalIdAsync(string externalId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(externalId)}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get customer: {StatusCode}", response.StatusCode);
                return (false, null);
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var customer = ParseMaxioCustomer(jsonDoc.RootElement.GetProperty("customer"));

            return (true, customer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while getting customer");
            return (false, null);
        }
    }

    private MaxioCustomer ParseMaxioCustomer(JsonElement element)
    {
        return new MaxioCustomer
        {
            Id = element.GetProperty("id").GetInt32(),
            FirstName = element.GetProperty("first_name").GetString() ?? string.Empty,
            LastName = element.GetProperty("last_name").GetString() ?? string.Empty,
            Email = element.GetProperty("email").GetString() ?? string.Empty,
            Reference = element.TryGetProperty("reference", out var refProp) ? refProp.GetString() : string.Empty,
        };
    }

    private MaxioSubscription ParseMaxioSubscription(JsonElement element)
    {
        var nextBillingAtStr = element.TryGetProperty("next_billing_at", out var nbProp) ? nbProp.GetString() : null;
        var createdAtStr = element.TryGetProperty("created_at", out var caProp) ? caProp.GetString() : null;

        return new MaxioSubscription
        {
            Id = element.GetProperty("id").GetInt32(),
            CustomerId = element.GetProperty("customer_id").GetInt32(),
            ProductId = element.GetProperty("product_id").GetInt32(),
            State = element.GetProperty("state").GetString() ?? string.Empty,
            NextBillingAt = !string.IsNullOrEmpty(nextBillingAtStr) && DateTime.TryParse(nextBillingAtStr, out var nextBilling) ? nextBilling : null,
            CreatedAt = !string.IsNullOrEmpty(createdAtStr) && DateTime.TryParse(createdAtStr, out var created) ? created : DateTime.MinValue,
            CurrentPrice = element.TryGetProperty("current_period_started_at", out _) ?
                (element.TryGetProperty("product", out var prod) ?
                    (prod.TryGetProperty("default_price_point", out var dpp) ?
                        (decimal?)dpp.GetProperty("price").GetDecimal() : null) : null) : null,
        };
    }
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }
}

public class MaxioCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
}

public class MaxioSubscription
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal? CurrentPrice { get; set; }
}
