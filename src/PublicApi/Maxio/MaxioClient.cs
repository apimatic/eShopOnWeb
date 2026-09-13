using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
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
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<List<MaxioProduct>> ListProductsAsync(string? productFamilyHandle = null)
    {
        var url = "products.json";
        if (!string.IsNullOrEmpty(productFamilyHandle))
        {
            url += $"?handles[]={productFamilyHandle}";
        }

        _logger.LogInformation("Maxio: Listing products from {Url}", url);
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOptions);
        var result = new List<MaxioProduct>();
        if (products != null)
        {
            foreach (var p in products)
            {
                result.Add(p.Product);
            }
        }
        return result;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogInformation("Maxio: Looking up customer by reference {Reference}", reference);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var customerResponse = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions);
        return customerResponse?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string? reference = null)
    {
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerData
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        _logger.LogInformation("Maxio: Creating customer {Email} with reference {Reference}", email, reference);
        var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions);

        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Maxio: Customer creation returned 422: {Error}", errorContent);

            var errorResponse = await response.Content.ReadFromJsonAsync<MaxioErrorResponse>(JsonOptions);
            var errors = errorResponse?.Errors;
            if (errors != null && errors.Any(e => e.Code == "DuplicateReference"))
            {
                _logger.LogInformation("Maxio: Customer with reference {Reference} already exists, looking up", reference);
                return await FindCustomerByReferenceAsync(reference!)
                    ?? throw new InvalidOperationException($"Customer with reference {reference} was expected but not found.");
            }

            throw new HttpRequestException($"Maxio customer creation failed: {errorContent}");
        }

        response.EnsureSuccessStatusCode();
        var customerResponse = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOptions);
        return customerResponse!.Customer;
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string firstName, string lastName, string email, string reference)
    {
        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing != null)
        {
            _logger.LogInformation("Maxio: Found existing customer {Id} for reference {Reference}", existing.Id, reference);
            return existing;
        }

        _logger.LogInformation("Maxio: No customer found for reference {Reference}, creating new one", reference);
        return await CreateCustomerAsync(firstName, lastName, email, reference);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionData
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = "remittance"
            }
        };

        _logger.LogInformation("Maxio: Creating subscription for product {Product} and customer {Reference}", productHandle, customerReference);
        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio: Subscription creation failed with status {Status}: {Error}", response.StatusCode, errorContent);
            throw new HttpRequestException($"Maxio subscription creation failed ({response.StatusCode}): {errorContent}");
        }

        var subscriptionResponse = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(JsonOptions);
        return subscriptionResponse!.Subscription;
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        _logger.LogInformation("Maxio: Listing subscriptions for customer {CustomerId}", customerId);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var subscriptions = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOptions);
        var result = new List<MaxioSubscription>();
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
