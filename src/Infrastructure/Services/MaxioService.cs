using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API (https://developers.maxio.com) over a typed
/// HttpClient. The client's base address and Basic-auth header are configured at registration
/// time from <see cref="MaxioOptions"/> - see Program.cs.
/// </summary>
public class MaxioService : IMaxioService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}/products.json";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return envelopes.Select(e => e.Product).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(NewMaxioCustomer newCustomer, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", new NewMaxioCustomerEnvelope(newCustomer), cancellationToken);
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return envelopes.Select(e => e.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new NewMaxioSubscriptionEnvelope(new NewMaxioSubscription(productHandle, customerReference)),
            cancellationToken);
        return envelope.Subscription;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response body.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, body);
    }
}
