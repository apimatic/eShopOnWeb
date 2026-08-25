using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates subscription signups against Maxio Advanced Billing.
/// Maxio is the system of record; eShopOnWeb users are correlated to Maxio
/// customers through the customer "reference" (the eShopOnWeb user id).
/// </summary>
public class SubscriptionBillingService : ISubscriptionBillingService
{
    // Live states per the spec's Subscription-State enum in which a shopper is
    // considered already subscribed (so a retry/double-click must not create a
    // second subscription for the same plan).
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "awaiting_signup", "past_due", "soft_failure", "paused"
    };

    // Serializes subscribe attempts per user so concurrent double-clicks cannot
    // race past the existing-subscription check.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly IMaxioBillingClient _maxioClient;
    private readonly MaxioSettings _settings;

    public SubscriptionBillingService(IMaxioBillingClient maxioClient, MaxioSettings settings)
    {
        _maxioClient = maxioClient;
        _settings = settings;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            })
            .OrderBy(p => p.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionDetails?> SubscribeAsync(SubscriberInfo subscriber, string productHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            return null;
        }

        var userLock = UserLocks.GetOrAdd(subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            var subscriptions = await _maxioClient.ListSubscriptionsByCustomerAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(s =>
                string.Equals(s.ProductHandle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                LiveStates.Contains(s.State));

            if (existing is not null)
            {
                return Map(existing);
            }

            var created = await _maxioClient.CreateSubscriptionAsync(plan.Handle, subscriber.UserId, cancellationToken);
            return Map(created);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        var subscriptions = await _maxioClient.ListSubscriptionsByCustomerAsync(customer.Id, cancellationToken);
        return subscriptions.Select(Map).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberInfo subscriber, CancellationToken cancellationToken)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var firstName = subscriber.Email.Split('@')[0];
        try
        {
            return await _maxioClient.CreateCustomerAsync(firstName, "Subscriber", subscriber.Email, subscriber.UserId, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent signup that created the customer first;
            // the reference is unique per the spec, so re-read it.
            var existing = await _maxioClient.FindCustomerByReferenceAsync(subscriber.UserId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private static SubscriptionDetails Map(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.ProductInterval,
        IntervalUnit = subscription.ProductIntervalUnit,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
