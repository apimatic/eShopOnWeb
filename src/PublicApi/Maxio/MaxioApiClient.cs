using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioApiClient : IMaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings)
    {
        _httpClient = httpClient;

        // Configuration is applied lazily so the host can start (e.g. in tests) without
        // Maxio credentials; any actual API call without them fails fast with a clear error.
        var config = settings.Value;
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            _httpClient.BaseAddress = new Uri(config.GetBaseUrl() + "/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.ApiKey}:X")));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var url = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await ReadAsync<List<MaxioProductResponse>>(response, cancellationToken);
        return items.Where(i => i.Product != null).Select(i => i.Product!).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetSingleOrDefaultAsync<MaxioCustomerResponse>(url, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomer customer, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var request = new MaxioCustomerRequest { Customer = customer };
        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioCustomerResponse>(response, cancellationToken);
        return envelope.Customer ?? throw new MaxioApiException(HttpStatusCode.OK, "Create customer returned an empty customer object.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var items = await ReadAsync<List<MaxioSubscriptionResponse>>(response, cancellationToken);
        return items.Where(i => i.Subscription != null).Select(i => i.Subscription!).ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetSingleOrDefaultAsync<MaxioSubscriptionResponse>(url, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionRequestItem subscription, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var request = new MaxioSubscriptionRequest { Subscription = subscription };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return envelope.Subscription ?? throw new MaxioApiException(HttpStatusCode.OK, "Create subscription returned an empty subscription object.");
    }

    private void EnsureConfigured()
    {
        if (_httpClient.BaseAddress == null)
        {
            throw new InvalidOperationException(
                "Maxio is not configured. Set Maxio:ApiKey and Maxio:Subdomain (or Maxio:BaseUrl) " +
                "via user-secrets or environment variables.");
        }
    }

    private async Task<T?> GetSingleOrDefaultAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        EnsureConfigured();
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, body);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, "Response body was empty or not valid JSON.");
    }
}
