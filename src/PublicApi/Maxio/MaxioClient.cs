using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = ResolveBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<Product>> ListProductsForFamilyAsync()
    {
        var familyHandle = _settings.ProductFamilyHandle;
        _logger.LogInformation("Listing products for family handle: {FamilyHandle}", familyHandle);

        var response = await _httpClient.GetAsync($"/product_families/handle:{familyHandle}/products.json");
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<List<ProductResponse>>(JsonOptions);
        return products?.ConvertAll(p => p.Product) ?? new List<Product>();
    }

    public async Task<Customer?> FindCustomerByReferenceAsync(string reference)
    {
        _logger.LogInformation("Looking up customer by reference: {Reference}", reference);

        var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Customer lookup returned {StatusCode}", response.StatusCode);
            return null;
        }

        var customerResponse = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions);
        return customerResponse?.Customer;
    }

    public async Task<Customer> CreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        _logger.LogInformation("Creating Maxio customer for {Email} with reference {Reference}", email, reference);

        var request = new { customer = new { first_name = firstName, last_name = lastName, email, reference } };
        var response = await _httpClient.PostAsJsonAsync("/customers.json", request, JsonOptions);

        if (response.IsSuccessStatusCode)
        {
            var customerResponse = await response.Content.ReadFromJsonAsync<CustomerResponse>(JsonOptions);
            return customerResponse?.Customer ?? throw new InvalidOperationException("Failed to deserialize customer response");
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogError("Create customer failed with {StatusCode}: {Error}", response.StatusCode, errorContent);
        throw new HttpRequestException($"Maxio create customer failed ({response.StatusCode}): {errorContent}");
    }

    public async Task<Customer> FindOrCreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Found existing customer {CustomerId} for reference {Reference}", existing.Id, reference);
            return existing;
        }

        return await CreateCustomerAsync(firstName, lastName, email, reference);
    }

    public async Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle, string? paymentCollectionMethod = null)
    {
        _logger.LogInformation("Creating subscription for customer {CustomerId}, product {ProductHandle}", customerId, productHandle);

        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                payment_collection_method = paymentCollectionMethod ?? "remittance"
            }
        };

        var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", body, JsonOptions);

        if (response.IsSuccessStatusCode)
        {
            var subResponse = await response.Content.ReadFromJsonAsync<SubscriptionResponse>(JsonOptions);
            return subResponse?.Subscription ?? throw new InvalidOperationException("Failed to deserialize subscription response");
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogError("Create subscription failed with {StatusCode}: {Error}", response.StatusCode, errorContent);
        throw new HttpRequestException($"Maxio create subscription failed ({response.StatusCode}): {errorContent}");
    }

    public async Task<List<Subscription>> ListSubscriptionsByCustomerIdAsync(int customerId)
    {
        _logger.LogInformation("Listing subscriptions for customer {CustomerId}", customerId);

        var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={customerId}");

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("List subscriptions failed with {StatusCode}: {Error}", response.StatusCode, errorContent);
            return new List<Subscription>();
        }

        var subscriptions = await response.Content.ReadFromJsonAsync<List<SubscriptionResponse>>(JsonOptions);
        return subscriptions?.ConvertAll(s => s.Subscription) ?? new List<Subscription>();
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            return _settings.BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new InvalidOperationException("Either Maxio:BaseUrl or Maxio:Subdomain must be configured.");
        }

        return $"https://{_settings.Subdomain}.chargify.com/";
    }
}
