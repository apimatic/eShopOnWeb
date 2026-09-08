using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Default <see cref="IMaxioSubscriptionService"/>. Maxio Advanced Billing remains the system of
/// record; this service adds the eShop-specific guarantee that a repeated subscribe for the same
/// user + plan is idempotent (no duplicate customer, no duplicate subscription).
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // States that mean a subscription is still "live" for idempotency purposes.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "authorization_period", "collection_period", "on_trial"
    };

    private readonly IMaxioApiClient _client;

    // The seeded plans are configured with "payment method not required" and no trial, so the
    // first period is invoiced rather than charged immediately. "remittance" (spec Collection-Method
    // enum) tells Advanced Billing to open the invoice and NOT attempt an automatic charge, which
    // is what lets a shopper subscribe without card capture / 3-DS.
    private const string SubscriptionPaymentCollectionMethod = "remittance";

    public MaxioSubscriptionService(IMaxioApiClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(string productFamilyHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A Product Family handle is required.", nameof(productFamilyHandle));
        }

        var products = await _client.ListProductsAsync(ct);

        return products
            .Where(p => !p.Archived)
            .Where(p => string.Equals(p.ProductFamilyHandle, productFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<Subscription> SubscribeAsync(MaxioCustomerInput customer, string planHandle, CancellationToken ct = default)
    {
        if (customer is null || string.IsNullOrWhiteSpace(customer.Reference))
        {
            throw new ArgumentException("A customer reference (the eShop user identity) is required.", nameof(customer));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan (product) handle is required.", nameof(planHandle));
        }

        var maxioCustomer = await EnsureCustomerAsync(customer, ct);

        // Deterministic subscription reference: idempotency key for the (user, plan) pair.
        var subscriptionReference = $"{customer.Reference}:{planHandle}";

        // 1) Fast path: an explicit prior create with this reference.
        var byReference = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, ct);
        if (byReference is not null)
        {
            return byReference;
        }

        // 2) Fallback path: any live subscription the customer already has on this plan.
        var existing = await _client.ListSubscriptionsForCustomerAsync(maxioCustomer.Id, ct);
        var alreadyEnrolled = existing.FirstOrDefault(s =>
            string.Equals(s.ProductHandle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            LiveStates.Contains(s.State));
        if (alreadyEnrolled is not null)
        {
            return alreadyEnrolled;
        }

        // 3) Create it.
        try
        {
            return await _client.CreateSubscriptionAsync(new MaxioSubscriptionInput
            {
                CustomerId = maxioCustomer.Id,
                ProductHandle = planHandle,
                Reference = subscriptionReference,
                PaymentCollectionMethod = SubscriptionPaymentCollectionMethod
            }, ct);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // A concurrent double-click may have created it a moment earlier — reconcile.
            var reconciled = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken ct = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, ct);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _client.ListSubscriptionsForCustomerAsync(customer.Id, ct);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(MaxioCustomerInput input, CancellationToken ct)
    {
        var existing = await _client.FindCustomerByReferenceAsync(input.Reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(input, ct);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            // "reference has already been taken" — a concurrent request created it. Re-read.
            var raced = await _client.FindCustomerByReferenceAsync(input.Reference, ct);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }
}
