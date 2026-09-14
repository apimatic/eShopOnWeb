using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioApiClient"/>, built strictly against the endpoints,
/// request/response shapes and auth scheme documented in maxio-spec/openapi.yaml. The injected
/// <see cref="HttpClient"/> already has its BaseAddress and Basic-Auth header configured (see
/// Infrastructure.Dependencies.ConfigureServices).
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("customers.json", new MaxioCreateCustomerEnvelope { Customer = request }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        const int perPage = 200;

        for (var page = 1; ; page++)
        {
            using var response = await _httpClient.GetAsync($"products.json?page={page}&per_page={perPage}", cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(cancellationToken: cancellationToken)
                ?? new List<MaxioProductEnvelope>();

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < perPage)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", new MaxioCreateSubscriptionEnvelope { Subscription = request }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty subscription payload.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(cancellationToken: cancellationToken)
            ?? new List<MaxioSubscriptionEnvelope>();

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, $"Maxio API call to {response.RequestMessage?.RequestUri} failed with {(int)response.StatusCode}: {body}");
    }
}
