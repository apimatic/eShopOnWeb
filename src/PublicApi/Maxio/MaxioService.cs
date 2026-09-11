using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioService(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public static void ConfigureHttpClient(HttpClient client, MaxioSettings settings)
    {
        var baseUrl = settings.ResolveBaseUrl();
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync()
    {
        var familyHandle = _settings.ProductFamilyHandle;
        var url = $"/product_families/handle:{familyHandle}/products.json?per_page=200";

        _logger.LogInformation("Listing Maxio products for family {FamilyHandle}", familyHandle);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var products = JsonSerializer.Deserialize<List<MaxioProductListResponse>>(json, JsonOptions);

        var result = new List<MaxioProduct>();
        if (products != null)
        {
            foreach (var wrapper in products)
            {
                if (wrapper.Product.ArchivedAt == null)
                    result.Add(wrapper.Product);
            }
        }

        return result;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"/customers.json?reference={Uri.EscapeDataString(reference)}";

        _logger.LogInformation("Looking up Maxio customer by reference {Reference}", reference);

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to look up customer by reference {Reference}: {StatusCode}", reference, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var customers = JsonSerializer.Deserialize<List<MaxioCustomerResponse>>(json, JsonOptions);

        if (customers == null || customers.Count == 0)
            return null;

        return customers[0].Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        _logger.LogInformation("Creating Maxio customer {Email} with reference {Reference}", email, reference);

        var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create customer: {StatusCode} - {Body}", response.StatusCode, responseJson);
            response.EnsureSuccessStatusCode();
        }

        var customerResponse = JsonSerializer.Deserialize<MaxioCustomerResponse>(responseJson, JsonOptions);
        return customerResponse!.Customer;
    }

    public async Task<MaxioCustomer> FindOrCreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Found existing Maxio customer {CustomerId} for reference {Reference}", existing.Id, reference);
            return existing;
        }

        return await CreateCustomerAsync(firstName, lastName, email, reference);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"/customers/{customerId}/subscriptions.json";

        _logger.LogInformation("Listing subscriptions for Maxio customer {CustomerId}", customerId);

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to list customer subscriptions: {StatusCode}", response.StatusCode);
            return Array.Empty<MaxioSubscription>();
        }

        var json = await response.Content.ReadAsStringAsync();
        var subscriptions = JsonSerializer.Deserialize<List<MaxioSubscriptionListResponse>>(json, JsonOptions);

        var result = new List<MaxioSubscription>();
        if (subscriptions != null)
        {
            foreach (var wrapper in subscriptions)
            {
                result.Add(wrapper.Subscription);
            }
        }

        return result;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance"
            }
        };

        _logger.LogInformation("Creating Maxio subscription for product {ProductHandle}, customer {CustomerId}", productHandle, customerId);

        var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/subscriptions.json", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to create subscription: {StatusCode} - {Body}", response.StatusCode, responseJson);
            response.EnsureSuccessStatusCode();
        }

        var subscriptionResponse = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseJson, JsonOptions);
        return subscriptionResponse!.Subscription;
    }
}
