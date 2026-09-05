using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin REST client for the Maxio Advanced Billing API. The <see cref="HttpClient"/> injected here
/// is configured (BaseAddress + Basic Auth credentials) by the DI registration in Dependencies.cs.
/// Endpoint paths and JSON shapes are confirmed against the shipped Maxio.AdvancedBillingSdk source.
/// </summary>
internal class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>GET /customers/lookup.json?reference={reference} - returns null when no customer has that reference.</summary>
    public async Task<CustomerPayload?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>POST /customers.json</summary>
    public async Task<CustomerPayload> CreateCustomerAsync(string email, string firstName, string lastName, string reference, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerPayload
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("/customers.json", body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope!.Customer!;
    }

    /// <summary>GET /product_families/handle:{familyHandle}/products.json</summary>
    public async Task<IReadOnlyList<ProductPayload>> ListProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            $"/product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json", cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken);
        return envelopes!.Where(e => e.Product is not null).Select(e => e.Product!).ToList();
    }

    /// <summary>POST /subscriptions.json for an existing customer.</summary>
    public async Task<SubscriptionPayload> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle,
                // The sandbox plans require no payment method; "remittance" signs the subscription up
                // without requiring or auto-charging a stored card. See PaymentCollectionMethod doc comment.
                PaymentCollectionMethod = "remittance"
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("/subscriptions.json", body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        return envelope!.Subscription!;
    }

    /// <summary>GET /customers/{customer_id}/subscriptions.json</summary>
    public async Task<IReadOnlyList<SubscriptionPayload>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<SubscriptionPayload>();
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken);
        return envelopes!.Where(e => e.Subscription is not null).Select(e => e.Subscription!).ToList();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, $"Maxio API call to {response.RequestMessage?.RequestUri} failed with {(int)response.StatusCode} {response.StatusCode}: {body}");
    }
}
