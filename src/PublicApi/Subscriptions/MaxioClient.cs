using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioClient : IMaxioClient
{
    private const int PageSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();

        for (var page = 1; ; page++)
        {
            using var response = await SendGetAsync(
                $"products.json?page={page}&per_page={PageSize}&include_archived=false",
                cancellationToken);
            var envelopes = await DeserializeAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
            products.AddRange(envelopes.Select(x => x.Product));

            if (envelopes.Count < PageSize)
            {
                return products;
            }
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        using var response = await SendGetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken,
            allowNotFound: true);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return (await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateMaxioCustomer customer,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json",
            new CreateMaxioCustomerRequest { Customer = customer },
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        using var response = await SendGetAsync(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);
        var envelopes = await DeserializeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return envelopes.Select(x => x.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateMaxioSubscription subscription,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "subscriptions.json",
            new CreateMaxioSubscriptionRequest { Subscription = subscription },
            JsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken)).Subscription;
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        string requestUri,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
            {
                return response;
            }

            if (attempt < 3 && response.StatusCode is HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                continue;
            }

            try
            {
                await EnsureSuccessAsync(response, cancellationToken);
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new MaxioApiException(
            response.StatusCode,
            "Maxio returned an empty or invalid response.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 4096)
        {
            body = body[..4096];
        }

        throw new MaxioApiException(
            response.StatusCode,
            $"Maxio rejected the request with HTTP {(int)response.StatusCode}.",
            body);
    }
}
