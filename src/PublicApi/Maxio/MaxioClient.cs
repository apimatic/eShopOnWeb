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

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = !string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? _options.BaseUrl.TrimEnd('/')
            : $"https://{_options.Subdomain}.chargify.com";

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string? productFamilyHandle = null)
    {
        var url = "products.json";
        if (!string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            url += $"?filters[product_family_handle]={Uri.EscapeDataString(productFamilyHandle)}";
        }

        _logger.LogDebug("Listing Maxio products from {Url}", url);
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var products = JsonSerializer.Deserialize<List<MaxioProductResponse>>(content, s_jsonOptions);
        return products?.ConvertAll(p => p.Product) ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogDebug("Looking up Maxio customer by reference {Reference}", reference);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(content, s_jsonOptions);
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var json = JsonSerializer.Serialize(request, s_jsonOptions);
        _logger.LogDebug("Creating Maxio customer with reference {Reference}", reference);

        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("customers.json", httpContent);

        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning("Customer with reference {Reference} may already exist, attempting lookup", reference);
            return await FindCustomerByReferenceAsync(reference)
                ?? throw new InvalidOperationException($"Failed to create or find customer with reference {reference}");
        }

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioCustomerResponse>(content, s_jsonOptions);
        return result?.Customer ?? throw new InvalidOperationException("Failed to deserialize customer response");
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Found existing Maxio customer {CustomerId} for reference {Reference}", existing.Id, reference);
            return existing;
        }

        _logger.LogInformation("Creating new Maxio customer for reference {Reference}", reference);
        return await CreateCustomerAsync(firstName, lastName, email, reference);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance"
            }
        };

        var json = JsonSerializer.Serialize(request, s_jsonOptions);
        _logger.LogDebug("Creating Maxio subscription for customer {CustomerId}, product {ProductHandle}", customerId, productHandle);

        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("subscriptions.json", httpContent);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(content, s_jsonOptions);
        return result?.Subscription ?? throw new InvalidOperationException("Failed to deserialize subscription response");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        _logger.LogDebug("Listing Maxio subscriptions for customer {CustomerId}", customerId);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var subscriptions = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(content, s_jsonOptions);
        return subscriptions?.ConvertAll(s => s.Subscription) ?? new List<MaxioSubscription>();
    }
}
