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
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<IReadOnlyList<Product>> GetProductsForFamilyAsync(string familyHandle);
    Task<Customer?> FindCustomerByReferenceAsync(string reference);
    Task<Customer> CreateCustomerAsync(string firstName, string lastName, string email, string? reference);
    Task<Subscription> CreateSubscriptionAsync(string productHandle, int customerId, string? reference = null);
    Task<IReadOnlyList<Subscription>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = string.IsNullOrEmpty(_options.BaseUrl)
            ? $"https://{_options.Subdomain}.chargify.com"
            : _options.BaseUrl.TrimEnd('/');

        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<Product>> GetProductsForFamilyAsync(string familyHandle)
    {
        var url = $"product_families/handle:{familyHandle}/products.json";
        _logger.LogInformation("Fetching products for family {FamilyHandle} from {Url}", familyHandle, url);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var products = JsonSerializer.Deserialize<List<ProductResponse>>(content, JsonOptions)
            ?? new List<ProductResponse>();

        return products.Select(p => p.Product).ToList();
    }

    public async Task<Customer?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogInformation("Looking up customer by reference {Reference}", reference);

        var response = await _httpClient.GetAsync(url);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CustomerResponse>(content, JsonOptions);
        return result?.Customer;
    }

    public async Task<Customer> CreateCustomerAsync(string firstName, string lastName, string email, string? reference)
    {
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomerData
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        _logger.LogInformation("Creating Maxio customer for {Email} with reference {Reference}", email, reference);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("customers.json", content);

        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Customer creation returned 422: {Error}", errorContent);
            throw new MaxioApiException((int)response.StatusCode, errorContent);
        }

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CustomerResponse>(responseContent, JsonOptions);
        return result!.Customer;
    }

    public async Task<Subscription> CreateSubscriptionAsync(string productHandle, int customerId, string? reference = null)
    {
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionData
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                PaymentCollectionMethod = "remittance",
                Reference = reference
            }
        };

        _logger.LogInformation("Creating subscription for product {ProductHandle}, customer {CustomerId}", productHandle, customerId);

        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("subscriptions.json", content);

        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Subscription creation returned 422: {Error}", errorContent);
            throw new MaxioApiException((int)response.StatusCode, errorContent);
        }

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<SubscriptionResponse>(responseContent, JsonOptions);
        return result!.Subscription;
    }

    public async Task<IReadOnlyList<Subscription>> GetCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        _logger.LogInformation("Fetching subscriptions for customer {CustomerId}", customerId);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var subscriptions = JsonSerializer.Deserialize<List<SubscriptionResponse>>(content, JsonOptions)
            ?? new List<SubscriptionResponse>();

        return subscriptions.Select(s => s.Subscription).ToList();
    }
}

public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public MaxioApiException(int statusCode, string responseBody)
        : base($"Maxio API returned {statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
