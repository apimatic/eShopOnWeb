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

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Hand-written client for the subset of Maxio Advanced Billing operations the subscription
/// capability needs. Every request/response shape below is taken directly from
/// maxio-spec/openapi.yaml; nothing here is invented beyond that contract.
/// </summary>
public class MaxioBillingClient : IMaxioBillingClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new InvalidOperationException(
                $"Configure '{MaxioOptions.ConfigSectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}' before listing plans.");
        }

        // GET /product_families/{product_family_id}.json - product_family_id may be the
        // numeric id or "handle:{handle}"; we always address families by handle.
        var handle = Uri.EscapeDataString(_options.ProductFamilyHandle);
        using var response = await _httpClient.GetAsync($"product_families/handle:{handle}/products.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await ReadAsAsync<List<ProductEnvelope>>(response, cancellationToken) ?? new List<ProductEnvelope>();
        return envelopes
            .Where(e => e.Product is not null)
            .Select(e => MapPlan(e.Product!))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerProfile profile, CancellationToken cancellationToken = default)
    {
        var payload = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Email = profile.Email,
                Reference = profile.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, MaxioJsonOptions.Default, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadAsAsync<CustomerEnvelope>(response, cancellationToken);
        if (envelope?.Customer is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty customer payload.");
        }

        return MapCustomer(envelope.Customer);
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await ReadAsAsync<SubscriptionEnvelope>(response, cancellationToken);
        return envelope?.Subscription is null ? null : MapSubscription(envelope.Subscription);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string subscriptionReference, CancellationToken cancellationToken = default)
    {
        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire
            {
                ProductHandle = planHandle,
                CustomerId = customerId,
                Reference = subscriptionReference,
                // The seeded plans don't require a payment method; "remittance" is the
                // Create-Subscription example the spec itself gives for signups with no
                // card on file (see the "Basic" example under POST /subscriptions.json).
                PaymentCollectionMethod = "remittance"
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, MaxioJsonOptions.Default, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await ReadAsAsync<SubscriptionEnvelope>(response, cancellationToken);
        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty subscription payload.");
        }

        return MapSubscription(envelope.Subscription);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelopes = await ReadAsAsync<List<SubscriptionEnvelope>>(response, cancellationToken) ?? new List<SubscriptionEnvelope>();
        return envelopes
            .Where(e => e.Subscription is not null)
            .Select(e => MapSubscription(e.Subscription!))
            .ToList();
    }

    private static Task<T?> ReadAsAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        => response.Content.ReadFromJsonAsync<T>(MaxioJsonOptions.Default, cancellationToken);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException(response.StatusCode, ExtractErrorMessage(body));
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Maxio API request failed with no response body.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ValueKind switch
                {
                    JsonValueKind.Array => string.Join("; ", errors.EnumerateArray().Select(e => e.ToString())),
                    JsonValueKind.Object => string.Join("; ", errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}")),
                    _ => errors.ToString()
                };
            }
        }
        catch (JsonException)
        {
            // Not a JSON error body (e.g. an HTML error page from a gateway) - fall through.
        }

        return body;
    }

    private static MaxioPlan MapPlan(ProductWire product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static MaxioCustomer MapCustomer(CustomerWire customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email ?? string.Empty,
        FirstName = customer.FirstName ?? string.Empty,
        LastName = customer.LastName ?? string.Empty
    };

    private static MaxioSubscription MapSubscription(SubscriptionWire subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? string.Empty,
        CustomerId = subscription.Customer?.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        ActivatedAt = subscription.ActivatedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt
    };
}
