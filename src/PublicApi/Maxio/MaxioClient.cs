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

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<List<MaxioProduct>> ListProductsAsync(string productFamilyHandle);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference);
    Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request);
    Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request);
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
    Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioClient(
        HttpClient httpClient,
        IOptions<MaxioOptions> options,
        ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Maxio:ApiKey is not configured. API calls will fail.");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.BaseAddress = new Uri(_options.EffectiveBaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation("MaxioClient initialized. BaseUrl={BaseUrl}, Subdomain={Subdomain}",
            _options.EffectiveBaseUrl, _options.Subdomain);
    }

    public async Task<List<MaxioProduct>> ListProductsAsync(string productFamilyHandle)
    {
        var url = $"product_families/handle:{productFamilyHandle}/products.json";
        _logger.LogInformation("Maxio: GET {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var products = JsonSerializer.Deserialize<List<MaxioProductListResponse>>(content, JsonOptions);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maxio: Failed to list products for family {Handle}", productFamilyHandle);
            throw;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogInformation("Maxio: GET customers/lookup ref={Reference}", reference);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>();
        return result?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomerRequest request)
    {
        _logger.LogInformation("Maxio: POST customers.json ref={Reference}", request.Customer.Reference);

        var response = await _httpClient.PostAsJsonAsync("customers.json", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>();
        return result!.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscriptionRequest request)
    {
        _logger.LogInformation("Maxio: POST subscriptions.json product={ProductHandle}", request.Subscription.ProductHandle);

        var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>();
        return result!.Subscription;
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        _logger.LogInformation("Maxio: GET {Url}", url);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var subscriptions = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(content, JsonOptions);
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

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference)
    {
        var url = $"subscriptions.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogInformation("Maxio: GET subscriptions?reference={Reference}", reference);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var subscriptions = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(content, JsonOptions);

        if (subscriptions != null && subscriptions.Count > 0)
            return subscriptions[0].Subscription;

        return null;
    }
}
