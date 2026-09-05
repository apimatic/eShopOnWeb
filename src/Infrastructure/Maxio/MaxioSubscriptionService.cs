using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb subscribe flow against Maxio: ensure-customer, then
/// ensure-subscription, both keyed off a deterministic reference derived from the buyer's
/// identity so repeated calls (e.g. a double-click) never create duplicates.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioApiClient _client;

    public MaxioSubscriptionService(IMaxioApiClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForConfiguredFamilyAsync(cancellationToken);
        return products.Select(ToPlan).ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(string buyerId, string buyerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw new ArgumentException("Buyer id is required.", nameof(buyerId));
        if (string.IsNullOrWhiteSpace(planHandle)) throw new ArgumentException("Plan handle is required.", nameof(planHandle));

        var customerReference = BuildCustomerReference(buyerId);
        var customer = await EnsureCustomerAsync(customerReference, buyerEmail, cancellationToken);

        var subscriptionReference = BuildSubscriptionReference(buyerId, planHandle);
        var existing = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return ToEnrollment(existing, alreadyExisted: true);
        }

        try
        {
            var created = await _client.CreateSubscriptionWithoutPaymentMethodAsync(customer.Reference ?? customerReference, planHandle, subscriptionReference, cancellationToken);
            return ToEnrollment(created, alreadyExisted: false);
        }
        catch (MaxioApiException)
        {
            // A concurrent request (e.g. a genuine double-click) may have won the race and
            // created the subscription first; Maxio's reference lookup is the idempotency
            // source of truth, so check it before surfacing the original failure.
            var concurrent = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (concurrent is not null)
            {
                return ToEnrollment(concurrent, alreadyExisted: true);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw new ArgumentException("Buyer id is required.", nameof(buyerId));

        var customer = await _client.FindCustomerByReferenceAsync(BuildCustomerReference(buyerId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionEnrollment>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(s => ToEnrollment(s, alreadyExisted: true)).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, string buyerEmail, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(buyerEmail);

        try
        {
            return await _client.CreateCustomerAsync(customerReference, buyerEmail, firstName, lastName, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference collision from a concurrent request; the customer now exists.
            var raceWinner = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raceWinner is not null)
            {
                return raceWinner;
            }

            throw;
        }
    }

    private static string BuildCustomerReference(string buyerId) => $"eshoponweb:{buyerId}";

    private static string BuildSubscriptionReference(string buyerId, string planHandle) => $"eshoponweb:{buyerId}:{planHandle}";

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = Capitalize(parts.ElementAtOrDefault(0)) ?? "eShopOnWeb";
        var lastName = Capitalize(parts.ElementAtOrDefault(1)) ?? "Customer";
        return (firstName, lastName);
    }

    private static string? Capitalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        Price = product.PriceInCents / 100m,
        IntervalCount = product.Interval,
        IntervalUnit = product.IntervalUnit,
        ProductFamilyHandle = product.ProductFamily.Handle
    };

    private static SubscriptionEnrollment ToEnrollment(MaxioSubscription subscription, bool alreadyExisted) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product.Handle ?? string.Empty,
        PlanName = subscription.Product.Name,
        Price = subscription.ProductPriceInCents / 100m,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        AlreadyExisted = alreadyExisted
    };
}
