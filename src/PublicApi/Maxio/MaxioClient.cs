using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing API.
/// Auth is HTTP Basic with the API key as username and "X" as password (per Maxio docs).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _settings = settings.Value;
        _settings.Validate();
        _httpClient.BaseAddress = new Uri(_settings.GetBaseUrl() + "/");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            using var response = await _httpClient.GetAsync($"products.json?page={page}&per_page=200", cancellationToken);
            var wrappers = await ReadAsync<List<MaxioProductWrapper>>(response, cancellationToken);
            if (wrappers is null || wrappers.Count == 0)
            {
                break;
            }

            products.AddRange(wrappers.Select(w => w.Product));
            if (wrappers.Count < 200)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var wrapper = await ReadAsync<MaxioCustomerWrapper>(response, cancellationToken);
        return wrapper?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes customer, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json", new MaxioCreateCustomerRequest { Customer = customer }, JsonOptions, cancellationToken);

        var wrapper = await ReadAsync<MaxioCustomerWrapper>(response, cancellationToken);
        return wrapper!.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        var wrappers = await ReadAsync<List<MaxioSubscriptionWrapper>>(response, cancellationToken);
        return wrappers?.Select(w => w.Subscription).ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var payload = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(_settings.CollectionMethod) ? null : _settings.CollectionMethod
            },
            UniquenessToken = uniquenessToken
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, JsonOptions, cancellationToken);
        var wrapper = await ReadAsync<MaxioSubscriptionWrapper>(response, cancellationToken);
        return wrapper!.Subscription;
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Maxio API request failed: {StatusCode} {Body}", (int)response.StatusCode, body);
            throw new MaxioApiException(response.StatusCode, body,
                $"Maxio API returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }
}
