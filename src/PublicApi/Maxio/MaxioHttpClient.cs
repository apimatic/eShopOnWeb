using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioHttpClient : IMaxioHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioHttpClient> _logger;
    private readonly MaxioOptions _options;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MaxioHttpClient(HttpClient httpClient, ILogger<MaxioHttpClient> logger, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<List<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle)
    {
        var url = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json";
        _logger.LogDebug("Maxio GET {Url}", url);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var wrappers = await response.Content.ReadFromJsonAsync<List<MaxioProductResponse>>(JsonOpts);
        var products = new List<MaxioProduct>();
        if (wrappers != null)
        {
            foreach (var w in wrappers)
            {
                if (w.Product.ArchivedAt == null)
                    products.Add(w.Product);
            }
        }
        return products;
    }

    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        _logger.LogDebug("Maxio GET {Url}", url);

        var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var wrapper = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOpts);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request)
    {
        var url = "customers.json";
        _logger.LogDebug("Maxio POST {Url}", url);

        var response = await _httpClient.PostAsJsonAsync(url, request, JsonOpts);
        response.EnsureSuccessStatusCode();

        var wrapper = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(JsonOpts);
        return wrapper!.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request)
    {
        var url = "subscriptions.json";
        _logger.LogDebug("Maxio POST {Url}", url);

        var response = await _httpClient.PostAsJsonAsync(url, request, JsonOpts);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Maxio subscription creation failed: {StatusCode} {Body}", response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        var wrapper = JsonSerializer.Deserialize<MaxioSubscriptionResponse>(body, JsonOpts);
        return wrapper!.Subscription;
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json";
        _logger.LogDebug("Maxio GET {Url}", url);

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var wrappers = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionResponse>>(JsonOpts);
        var subscriptions = new List<MaxioSubscription>();
        if (wrappers != null)
        {
            foreach (var w in wrappers)
            {
                subscriptions.Add(w.Subscription);
            }
        }
        return subscriptions;
    }
}
