using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = _settings.ResolveBaseUrl();
        var apiKey = _settings.ApiKey;

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<MaxioProduct?> GetProductByHandleAsync(string handle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"products/handle/{handle}.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<MaxioProductResponse>(json, JsonOptions);
                return result?.Product;
            }

            _logger.LogWarning("Maxio GetProductByHandle returned {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Maxio product by handle {Handle}", handle);
            throw;
        }
    }

    public async Task<MaxioProduct?> GetProductByIdAsync(int productId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"products/{productId}.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<MaxioProductResponse>(json, JsonOptions);
                return result?.Product;
            }

            _logger.LogWarning("Maxio GetProductById returned {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Maxio product by ID {ProductId}", productId);
            throw;
        }
    }

    public async Task<List<MaxioProduct>> ListProductsByFamilyAsync(string productFamilyHandle)
    {
        try
        {
            var families = await ListProductFamiliesAsync();
            var family = families.Find(f => f.Handle == productFamilyHandle);
            if (family == null)
            {
                _logger.LogWarning("Maxio product family with handle {Handle} not found", productFamilyHandle);
                return new List<MaxioProduct>();
            }

            var response = await _httpClient.GetAsync($"product_families/{family.Id}/products.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var wrappers = JsonSerializer.Deserialize<List<MaxioProductResponse>>(json, JsonOptions);
                return wrappers?.ConvertAll(w => w.Product) ?? new List<MaxioProduct>();
            }

            _logger.LogWarning("Maxio ListProductsByFamily returned {StatusCode}", response.StatusCode);
            return new List<MaxioProduct>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Maxio products for family {Handle}", productFamilyHandle);
            throw;
        }
    }

    public async Task<List<MaxioProductFamily>> ListProductFamiliesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("product_families.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var wrappers = JsonSerializer.Deserialize<List<MaxioProductFamilyResponse>>(json, JsonOptions);
                return wrappers?.ConvertAll(w => w.ProductFamily) ?? new List<MaxioProductFamily>();
            }

            _logger.LogWarning("Maxio ListProductFamilies returned {StatusCode}", response.StatusCode);
            return new List<MaxioProductFamily>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Maxio product families");
            throw;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(json, JsonOptions);
                return result?.Customer;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            _logger.LogWarning("Maxio FindCustomerByReference returned {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding Maxio customer by reference {Reference}", reference);
            throw;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        try
        {
            var request = new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomerAttributes
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = reference
                }
            };

            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("customers.json", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(responseJson, JsonOptions);
                return result!.Customer;
            }

            // 422 = validation error, may indicate customer already exists — try lookup
            if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogInformation("Customer creation returned 422, attempting lookup by reference {Reference}", reference);
                var existing = await FindCustomerByReferenceAsync(reference);
                if (existing != null) return existing;
            }

            _logger.LogError("Maxio CreateCustomer returned {StatusCode}: {Body}", response.StatusCode, responseJson);
            throw new InvalidOperationException($"Maxio customer creation failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Maxio customer {Email}", email);
            throw;
        }
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Found existing Maxio customer {Id} for reference {Reference}", existing.Id, reference);
            return existing;
        }

        return await CreateCustomerAsync(firstName, lastName, email, reference);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId)
    {
        try
        {
            var request = new MaxioCreateSubscriptionRequest
            {
                Subscription = new MaxioCreateSubscriptionAttributes
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = "remittance"
                }
            };

            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("subscriptions.json", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseJson, JsonOptions);
                return result!.Subscription;
            }

            _logger.LogError("Maxio CreateSubscription returned {StatusCode}: {Body}", response.StatusCode, responseJson);
            throw new InvalidOperationException($"Maxio subscription creation failed: {response.StatusCode} - {responseJson}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Maxio subscription for product {ProductHandle}, customer {CustomerId}", productHandle, customerId);
            throw;
        }
    }

    public async Task<List<MaxioSubscription>> ListSubscriptionsForCustomerAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var wrappers = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(json, JsonOptions);
                return wrappers?.ConvertAll(w => w.Subscription) ?? new List<MaxioSubscription>();
            }

            _logger.LogWarning("Maxio ListSubscriptionsForCustomer returned {StatusCode}", response.StatusCode);
            return new List<MaxioSubscription>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Maxio subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"subscriptions/{subscriptionId}.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(json, JsonOptions);
                return result?.Subscription;
            }

            _logger.LogWarning("Maxio GetSubscription returned {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Maxio subscription {SubscriptionId}", subscriptionId);
            throw;
        }
    }
}
