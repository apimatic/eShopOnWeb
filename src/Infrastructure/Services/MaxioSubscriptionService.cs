using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API directly over HTTP (Basic auth, per
/// https://developers.maxio.com/http/getting-started/authentication). The injected
/// <see cref="HttpClient"/> is expected to already have its BaseAddress and Authorization
/// header configured (see the AddHttpClient registration in PublicApi's Program.cs).
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states that represent a "live" (already-enrolled) subscription for
    // idempotency purposes. See https://maxio-chargify.zendesk.com/hc/en-us/articles/5404222005773-Subscription-States
    private static readonly HashSet<string> LiveSubscriptionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "awaiting_signup", "past_due", "unpaid", "soft_failure"
    };

    private static readonly JsonSerializerOptions WireJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familySegment = $"handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}";
        var envelopes = await GetAsync<List<ProductEnvelope>>($"product_families/{familySegment}/products.json", cancellationToken);

        return envelopes
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan(p!.Handle, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit))
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(MaxioCustomerProfile customer, string planHandle, CancellationToken cancellationToken = default)
    {
        var customerId = await EnsureCustomerAsync(customer, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var payload = new CreateSubscriptionEnvelope
        {
            Subscription = new CreateSubscriptionWire
            {
                ProductHandle = planHandle,
                CustomerReference = customer.Reference,
                // "remittance" enrolls the subscriber without collecting a payment method,
                // matching the seeded plans' "payment method not required" configuration.
                PaymentCollectionMethod = "remittance"
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", payload, WireJsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request (e.g. a double-click) may have created the subscription
            // between our idempotency check above and this call; re-check before failing.
            var raced = await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException($"Maxio rejected the subscription for plan '{planHandle}': {errorBody}", (int)response.StatusCode);
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(WireJsonOptions, cancellationToken);
        if (created?.Subscription is null)
        {
            throw new MaxioApiException("Maxio returned an empty subscription response.", (int)response.StatusCode);
        }

        return MapSubscription(created.Subscription);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customerId = await FindCustomerIdByReferenceAsync(customerReference, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var envelopes = await GetAsync<List<SubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSubscription(s!))
            .ToList();
    }

    private async Task<long> EnsureCustomerAsync(MaxioCustomerProfile customer, CancellationToken cancellationToken)
    {
        var existingId = await FindCustomerIdByReferenceAsync(customer.Reference, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var payload = new CreateCustomerEnvelope
        {
            Customer = new CreateCustomerWire
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Reference = customer.Reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", payload, WireJsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio only allows one customer per reference value; a concurrent request may
            // have created it already (e.g. a double-click). Re-check before failing.
            var racedId = await FindCustomerIdByReferenceAsync(customer.Reference, cancellationToken);
            if (racedId is not null)
            {
                return racedId.Value;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException($"Maxio rejected customer creation for reference '{customer.Reference}': {errorBody}", (int)response.StatusCode);
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var created = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(WireJsonOptions, cancellationToken);
        if (created?.Customer is null)
        {
            throw new MaxioApiException("Maxio returned an empty customer response.", (int)response.StatusCode);
        }

        return created.Customer.Id;
    }

    private async Task<long?> FindCustomerIdByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);

        var found = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(WireJsonOptions, cancellationToken);
        return found?.Customer?.Id;
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<SubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        var match = envelopes
            .Select(e => e.Subscription)
            .FirstOrDefault(s => s is not null
                && string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && LiveSubscriptionStates.Contains(s.State));

        return match is null ? null : MapSubscription(match);
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<T>(WireJsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException($"Maxio returned an empty response for '{relativeUrl}'.", (int)response.StatusCode);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new MaxioApiException($"Maxio API call failed with status {(int)response.StatusCode}: {body}", (int)response.StatusCode);
    }

    private static CustomerSubscription MapSubscription(SubscriptionWire wire) => new(
        SubscriptionId: wire.Id,
        State: wire.State,
        PlanHandle: wire.Product?.Handle ?? string.Empty,
        PlanName: wire.Product?.Name ?? string.Empty,
        PriceInCents: wire.Product?.PriceInCents ?? 0,
        NextBillingDate: wire.NextAssessmentAt,
        CurrentPeriodEndsAt: wire.CurrentPeriodEndsAt,
        ActivatedAt: wire.ActivatedAt);
}
