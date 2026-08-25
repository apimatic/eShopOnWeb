using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        // Product family can be addressed by handle using the "handle:" prefix.
        var url = $"product_families/handle:{_settings.ProductFamilyHandle}/products.json?per_page=200";
        var products = await GetAsync<List<MaxioProductWrapper>>(url, cancellationToken);
        return products
            .Select(p => p.Product)
            .Where(p => p.ArchivedAt is null)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await ReadAsync<MaxioCustomerWrapper>(response, cancellationToken);
        return wrapper.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateCustomerRequest { Customer = customer };
        var wrapper = await PostAsync<MaxioCreateCustomerRequest, MaxioCustomerWrapper>("customers.json", request, cancellationToken);
        return wrapper.Customer;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string? reference, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                // eShopOnWeb captures no card details (PCI scope), so subscriptions are billed by
                // invoice ("remittance") instead of automatic collection, which would require a
                // payment method on file.
                PaymentCollectionMethod = "remittance"
            },
            UniquenessToken = uniquenessToken
        };
        var wrapper = await PostAsync<MaxioCreateSubscriptionRequest, MaxioSubscriptionWrapper>("subscriptions.json", request, cancellationToken);
        return wrapper.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionWrapper>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return subscriptions.Select(s => s.Subscription).ToList();
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);
        return await ReadAsync<TResponse>(response, cancellationToken);
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio API request failed with status {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new MaxioApiException(response.StatusCode, body,
                $"Maxio API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
        if (result is null)
        {
            throw new MaxioApiException(response.StatusCode, body, "Maxio API returned an empty or unreadable response body.");
        }

        return result;
    }
}
