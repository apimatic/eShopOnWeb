using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Services.Maxio.Models;
using Microsoft.eShopWeb.PublicApi.Settings;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services.Maxio;

public class MaxioApi : IMaxioApi
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioApi(HttpClient httpClient, IOptions<MaxioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        string family = _settings.GetProductFamilyPathSegment();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/product_families/{EscapePathSegment(family)}/products.json");
        var body = await SendAndReadAsync(request, cancellationToken);
        var items = JsonSerializer.Deserialize<List<MaxioProductResponse>>(body, MaxioJson.Options);
        return items?
            .Where(item => item.Product is not null)
            .Select(item => item.Product!)
            .ToList() ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<MaxioCustomerResponse>(body, MaxioJson.Options)?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer, CancellationToken cancellationToken)
    {
        var request = new MaxioCustomerRequest { Customer = customer };
        string payload = JsonSerializer.Serialize(request, MaxioJson.Options);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/customers.json")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var body = await SendAndReadAsync(httpRequest, cancellationToken);
        return JsonSerializer.Deserialize<MaxioCustomerResponse>(body, MaxioJson.Options)?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, body);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}");
        var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        return JsonSerializer.Deserialize<MaxioSubscriptionResponse>(body, MaxioJson.Options)?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionDraft subscription, CancellationToken cancellationToken)
    {
        var request = new MaxioSubscriptionRequest { Subscription = subscription };
        string payload = JsonSerializer.Serialize(request, MaxioJson.Options);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/subscriptions.json")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var body = await SendAndReadAsync(httpRequest, cancellationToken);
        return JsonSerializer.Deserialize<MaxioSubscriptionResponse>(body, MaxioJson.Options)?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, body);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/customers/{customerId}/subscriptions.json");
        var body = await SendAndReadAsync(request, cancellationToken);
        var items = JsonSerializer.Deserialize<List<MaxioSubscriptionResponse>>(body, MaxioJson.Options);
        return items?
            .Where(item => item.Subscription is not null)
            .Select(item => item.Subscription!)
            .ToList() ?? new List<MaxioSubscription>();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<string> SendAndReadAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await SendAsync(request, cancellationToken);
        var body = await ReadBodyAsync(response, cancellationToken);
        EnsureSuccess(response, body);
        return body;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, body);
        }
    }

    private static string EscapePathSegment(string segment) => Uri.EscapeDataString(segment);

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new MaxioNotConfiguredException(
                "Maxio Advanced Billing is not configured. Set the Maxio:ApiKey and Maxio:Subdomain settings (e.g. via user secrets from MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN).");
        }
    }
}
