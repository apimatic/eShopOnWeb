using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements the "Subscribe" hero flow against Maxio Advanced Billing: ensure-customer is
/// idempotent (keyed on the eShopOnWeb user id as the Maxio customer reference), and
/// subscribing is idempotent (a live subscription to the same plan is reused rather than
/// duplicated), so a double-click never creates two customers or two subscriptions.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states that represent an existing customer/plan relationship that should not
    // be duplicated by a repeat "subscribe" request. Excludes the end-of-life states (canceled,
    // expired, failed_to_create, trial_ended) for which re-subscribing is the correct action.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "trialing", "assessing", "active", "soft_failure", "past_due",
        "suspended", "paused", "unpaid", "on_hold", "awaiting_signup"
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
        var products = await _client.ListProductsAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null
                && string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnknownSubscriptionPlanException(planHandle);
        }

        var customer = await EnsureCustomerAsync(customerReference, email, firstName, lastName, cancellationToken);

        var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var live = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
            && LiveStates.Contains(s.State));

        if (live is not null)
        {
            return MapSubscription(live);
        }

        var created = await _client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            ProductHandle = planHandle,
            CustomerReference = customerReference
        }, cancellationToken);

        return MapSubscription(created);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(new MaxioCreateCustomer
            {
                Reference = customerReference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // A concurrent request (e.g. a double-click) may have created the customer for this
            // reference between our lookup and our create attempt - Maxio enforces reference
            // uniqueness, so re-fetch rather than surface a spurious failure.
            var racedCustomer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (racedCustomer is not null)
            {
                return racedCustomer;
            }

            throw;
        }
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Price = product.PriceInCents / 100m,
        IntervalCount = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        Plan = subscription.Product is not null ? MapPlan(subscription.Product) : new SubscriptionPlan(),
        NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt
    };
}
