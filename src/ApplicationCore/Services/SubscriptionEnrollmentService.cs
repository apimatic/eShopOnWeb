using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionEnrollmentService : ISubscriptionEnrollmentService
{
    private readonly IMaxioBillingClient _maxio;

    public SubscriptionEnrollmentService(IMaxioBillingClient maxio)
    {
        _maxio = maxio;
    }

    public Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
        => _maxio.ListPlansAsync(cancellationToken);

    public async Task<(MaxioSubscription Subscription, bool AlreadyExisted)> SubscribeAsync(
        MaxioCustomerProfile buyer, string planHandle, CancellationToken cancellationToken = default)
    {
        var customer = await EnsureCustomerAsync(buyer, cancellationToken);

        // A stable per-buyer-per-plan reference is what makes this idempotent: a double-click
        // (or a retried request) always resolves to the one subscription already on file.
        var subscriptionReference = BuildSubscriptionReference(buyer.Reference, planHandle);

        var existing = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return (existing, true);
        }

        try
        {
            var created = await _maxio.CreateSubscriptionAsync(customer.Id, planHandle, subscriptionReference, cancellationToken);
            return (created, false);
        }
        catch (MaxioApiException)
        {
            // A concurrent request may have won the race and created it first - check before failing.
            var raced = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced is not null)
            {
                return (raced, true);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _maxio.FindCustomerByReferenceAsync(buyerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(MaxioCustomerProfile buyer, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(buyer.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxio.CreateCustomerAsync(buyer, cancellationToken);
        }
        catch (MaxioApiException)
        {
            var raced = await _maxio.FindCustomerByReferenceAsync(buyer.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private static string BuildSubscriptionReference(string buyerReference, string planHandle)
        => $"eshop:{buyerReference}:{planHandle}";
}
