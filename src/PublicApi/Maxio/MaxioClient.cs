using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var baseUrl = _options.ResolveBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl + "/");

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<MaxioProductDto>> ListProductsAsync(string productFamilyHandle)
    {
        var url = $"product_families/handle:{productFamilyHandle}/products.json";
        _logger.LogInformation("Listing products for family {Handle}", productFamilyHandle);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOptions);
        var result = new List<MaxioProductDto>();
        if (products != null)
        {
            foreach (var p in products)
            {
                result.Add(p.Product);
            }
        }
        return result;
    }

    public async Task<MaxioProductDto?> GetProductByHandleAsync(string handle)
    {
        var url = $"products/handle/{handle}.json";
        _logger.LogInformation("Getting product by handle {Handle}", handle);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var productResponse = await response.Content.ReadFromJsonAsync<MaxioProductResponse>(JsonOptions);
        return productResponse?.Product;
    }

    public async Task<MaxioCustomerDto?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogInformation("Looking up customer by reference {Reference}", reference);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var customerResponse = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions);
        return customerResponse?.Customer;
    }

    public async Task<MaxioCustomerDto> CreateCustomerAsync(MaxioCreateCustomerRequest request)
    {
        var url = "customers.json";
        _logger.LogInformation("Creating customer {Email}", request.Customer.Email);

        var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio customer creation failed with status {Status}: {Body}",
                response.StatusCode, responseBody);
            throw new InvalidOperationException(
                $"Maxio customer creation failed ({response.StatusCode}): {responseBody}");
        }

        var customerResponse = JsonSerializer.Deserialize<MaxioCustomerResponse>(responseBody, JsonOptions);
        return customerResponse!.Customer;
    }

    public async Task<MaxioCustomerDto> EnsureCustomerAsync(string reference, string firstName, string lastName, string email)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Customer already exists with reference {Reference}, id={Id}", reference, existing.Id);
            return existing;
        }

        _logger.LogInformation("Creating new customer with reference {Reference}", reference);
        var createRequest = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerBody
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        return await CreateCustomerAsync(createRequest);
    }

    public async Task<MaxioSubscriptionDto> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request)
    {
        var url = "subscriptions.json";
        _logger.LogInformation("Creating subscription for product {ProductHandle}", request.Subscription.ProductHandle);

        var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio subscription creation failed with status {Status}: {Body}",
                response.StatusCode, responseBody);
            throw new InvalidOperationException(
                $"Maxio subscription creation failed ({response.StatusCode}): {responseBody}");
        }

        var subscriptionResponse = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(responseBody, JsonOptions);
        return subscriptionResponse!.Subscription;
    }

    public async Task<List<MaxioSubscriptionDto>> ListSubscriptionsAsync(string? state = null)
    {
        var url = "subscriptions.json";
        if (!string.IsNullOrWhiteSpace(state))
            url += $"?state={Uri.EscapeDataString(state)}";

        _logger.LogInformation("Listing subscriptions, state={State}", state ?? "all");

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var subscriptions = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions);
        var result = new List<MaxioSubscriptionDto>();
        if (subscriptions != null)
        {
            foreach (var s in subscriptions)
            {
                result.Add(s.Subscription);
            }
        }
        return result;
    }

    public async Task<MaxioSubscriptionDto?> FindSubscriptionByReferenceAsync(string reference)
    {
        var url = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogInformation("Looking up subscription by reference {Reference}", reference);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var subscriptionResponse = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions);
        return subscriptionResponse?.Subscription;
    }

    public async Task<List<MaxioSubscriptionDto>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        _logger.LogInformation("Listing subscriptions for customer {CustomerId}", customerId);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var subscriptions = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions);
        var result = new List<MaxioSubscriptionDto>();
        if (subscriptions != null)
        {
            foreach (var s in subscriptions)
            {
                result.Add(s.Subscription);
            }
        }
        return result;
    }
}
