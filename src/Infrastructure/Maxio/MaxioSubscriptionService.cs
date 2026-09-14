using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscription capability on top of <see cref="IMaxioGateway"/>, adding plan
/// validation and application-level idempotency (Maxio has no idempotency-key header — verified).
/// </summary>
internal sealed class MaxioSubscriptionService : ISubscriptionService
{
    // Maxio subscription states that represent a live enrolment. Used to decide whether a shopper is
    // already subscribed to a plan so that a repeated/double-clicked request does not create a duplicate.
    // States NOT listed here (canceled, expired, failed_to_create, trial_ended) are treated as inactive,
    // allowing a fresh subscription.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "soft_failure", "past_due", "on_hold", "suspended"
    };

    private readonly IMaxioGateway _gateway;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioGateway gateway, MaxioSettings settings, IAppLogger<MaxioSubscriptionService> logger)
    {
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _gateway.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .Select(ToPlan)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException("A plan handle is required to subscribe.");
        }

        // 1. Validate the requested plan against the configured catalog (no hard-coded handles).
        var products = await _gateway.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        var plan = products.FirstOrDefault(p =>
            p.ArchivedAt is null && string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            var available = string.Join(", ", products
                .Where(p => p.ArchivedAt is null && !string.IsNullOrEmpty(p.Handle))
                .Select(p => p.Handle));
            throw new PlanNotFoundException($"Plan '{planHandle}' is not available. Available plans: {available}.");
        }

        // 2. Ensure exactly one Maxio customer exists for this shopper (idempotent by reference).
        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        // 3. If a live subscription to this plan already exists, return it instead of creating a duplicate.
        var existingSubscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            s.State is not null && LiveStates.Contains(s.State));
        if (existing is not null)
        {
            _logger.LogInformation("Shopper {0} already has live subscription {1} to plan {2}; returning existing.",
                subscriber.Reference, existing.Id, plan.Handle!);
            return new SubscribeResult(ToSubscription(existing), alreadyExisted: true);
        }

        // 4. Create the subscription.
        var created = await _gateway.CreateSubscriptionAsync(customer.Id, plan.Handle!, cancellationToken);
        _logger.LogInformation("Created subscription {0} for shopper {1} on plan {2}.",
            created.Id, subscriber.Reference, plan.Handle!);
        return new SubscribeResult(ToSubscription(created), alreadyExisted: false);
    }

    public async Task<IReadOnlyList<SubscriberSubscription>> GetSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await _gateway.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriberSubscription>();
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscription).ToList();
    }

    /// <summary>
    /// Looks up the shopper's Maxio customer by stable reference and creates it if missing. Tolerates a
    /// create/create race (double-click) by re-looking-up on the unique-reference 422.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var existing = await _gateway.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(subscriber.Email, subscriber.Reference);
        try
        {
            var created = await _gateway.CreateCustomerAsync(subscriber.Reference, subscriber.Email, firstName, lastName, cancellationToken);
            _logger.LogInformation("Created Maxio customer {0} for shopper {1}.", created.Id, subscriber.Reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // The reference is unique per site, so a concurrent create loses this race. Re-lookup and use
            // whichever customer won.
            var raced = await _gateway.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }
    }

    private static (string firstName, string lastName) DeriveName(string email, string reference)
    {
        var local = email.Contains('@') ? email.Split('@')[0] : reference;
        var firstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        return (firstName, "eShopOnWeb");
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static SubscriberSubscription ToSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.Product?.PriceInCents ?? 0,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };
}
