using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio.Wire;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.ExternalServices.Maxio;

/// <summary>
/// Implements subscription enrollment against Maxio Advanced Billing. Maxio is the system of
/// record: nothing here is cached or persisted locally, so results always reflect live state.
///
/// The eShopOnWeb buyer's email (the app's username) is used as the Maxio customer's
/// <c>reference</c>, which Maxio guarantees is unique per customer. That gives us a natural,
/// idempotent join key without needing a local userId-to-Maxio-customer mapping table.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states from which re-subscribing to the same plan should be treated as a
    // fresh enrollment rather than a duplicate. Everything else (active, trialing, past_due,
    // etc.) is considered "already subscribed" for idempotency purposes.
    private static readonly HashSet<string> ReSubscribableStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(IMaxioApiClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => !string.IsNullOrEmpty(p.Handle))
            .Select(MapPlan)
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string buyerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail))
        {
            throw new ArgumentException("Buyer email is required.", nameof(buyerEmail));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("Plan handle is required.", nameof(planHandle));
        }

        // Validate against the family's actual catalog rather than trusting the caller-supplied
        // handle outright: Maxio would otherwise happily subscribe to any product handle on the
        // site, not just the ones eShopOnWeb intends to sell.
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customer = await EnsureCustomerAsync(buyerEmail, cancellationToken);

        var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            !ReSubscribableStates.Contains(s.State));
        if (existing is not null)
        {
            return MapSubscription(existing);
        }

        var created = await _client.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        return MapSubscription(created);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForBuyerAsync(string buyerEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerEmail))
        {
            throw new ArgumentException("Buyer email is required.", nameof(buyerEmail));
        }

        var customer = await _client.FindCustomerByReferenceAsync(buyerEmail, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string buyerEmail, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(buyerEmail, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var (firstName, lastName) = DeriveNameFromEmail(buyerEmail);
            return await _client.CreateCustomerAsync(firstName, lastName, buyerEmail, buyerEmail, cancellationToken);
        }
        catch (MaxioApiException)
        {
            // Maxio only allows one customer per reference. If a concurrent request (e.g. a
            // double-click) created the customer between our lookup and create attempt, this
            // will have failed with a duplicate-reference error - re-check before giving up, so
            // a race never surfaces as a user-facing failure.
            var retried = await _client.FindCustomerByReferenceAsync(buyerEmail, cancellationToken);
            if (retried is not null)
            {
                return retried;
            }
            throw;
        }
    }

    private static (string FirstName, string LastName) DeriveNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart, "Customer");
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        MaxioProductId = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        IntervalCount = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        IntervalCount = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
