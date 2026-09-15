using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyHandle = Uri.EscapeDataString(_options.ProductFamilyHandle!);
        var products = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/handle:{familyHandle}/products.json", cancellationToken);

        return products.Where(p => p.Product.ArchivedAt is null)
            .Select(p => p.Product)
            .ToArray();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await _httpClient.GetAsync(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(cancellationToken: cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var existing = await FindCustomerByReferenceAsync(attributes.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            "customers.json", new MaxioCustomerRequest { Customer = attributes }, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerResponse>(cancellationToken: cancellationToken);
            return envelope?.Customer ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
        }

        // A concurrent request may have won the unique-reference race.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            existing = await FindCustomerByReferenceAsync(attributes.Reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        throw new MaxioApiException(response.StatusCode);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await _httpClient.GetAsync(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(cancellationToken: cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var existing = await FindSubscriptionByReferenceAsync(attributes.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        using var response = await _httpClient.PostAsJsonAsync(
            "subscriptions.json", new MaxioSubscriptionRequest { Subscription = attributes }, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(cancellationToken: cancellationToken);
            return envelope?.Subscription ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
        }

        // A concurrent request may have won the unique-reference race.
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            existing = await FindSubscriptionByReferenceAsync(attributes.Reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }
        }

        throw new MaxioApiException(response.StatusCode);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var subscriptions = await GetAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return subscriptions.Select(item => item.Subscription).ToArray();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new MaxioApiException(HttpStatusCode.BadGateway);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio billing is not configured.");
        }

        // Set this at call time so Maxio:BaseUrl always wins over the derived address.
        _httpClient.BaseAddress = _options.GetBaseAddress();
        if (_httpClient.DefaultRequestHeaders.Authorization is null)
        {
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:X"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode);
        }
    }
}
