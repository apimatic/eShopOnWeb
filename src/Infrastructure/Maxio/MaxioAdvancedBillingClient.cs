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
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioAdvancedBillingClient : IMaxioAdvancedBillingClient
{
    private readonly HttpClient _httpClient;

    public MaxioAdvancedBillingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var familyId = $"handle:{productFamilyHandle}";
        var path = $"product_families/{familyId}/products.json?per_page=200&include_archived=false";
        var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return envelopes?.Select(e => e.Product).Where(p => p is not null).Cast<MaxioProduct>().ToList()
               ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateMaxioCustomer customer, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            HttpMethod.Post,
            "customers.json",
            new CreateMaxioCustomerRequest(customer),
            cancellationToken);

        return Require(envelope?.Customer, "Maxio did not return a customer.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);

        return envelopes?.Select(e => e.Subscription).Where(s => s is not null).Cast<MaxioSubscription>().ToList()
               ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, null, cancellationToken, allowNotFound: true);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateMaxioSubscription subscription, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Post,
            "subscriptions.json",
            new CreateMaxioSubscriptionRequest(subscription),
            cancellationToken);

        return Require(envelope?.Subscription, "Maxio did not return a subscription.");
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: MaxioJson.Options);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioBillingException((int)response.StatusCode, SummarizeErrors(payload, (int)response.StatusCode));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(payload, MaxioJson.Options);
    }

    private static T Require<T>(T? value, string message) where T : class
    {
        if (value is null)
        {
            throw new MaxioBillingException(502, message);
        }

        return value;
    }

    private static string SummarizeErrors(string payload, int statusCode)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"Maxio Advanced Billing request failed with HTTP {statusCode}.";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.String => errors.GetString() ?? payload,
                    JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(e => e.ToString())),
                    JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}")),
                    _ => payload
                };
            }
        }
        catch (JsonException)
        {
            // Fall through and return a truncated raw body.
        }

        return payload.Length <= 500 ? payload : payload[..500];
    }
}
