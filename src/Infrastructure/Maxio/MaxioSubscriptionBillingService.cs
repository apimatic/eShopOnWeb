using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio.Wire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements recurring-subscription billing against Maxio Advanced Billing. Orchestrates the
/// Maxio API calls (ensure customer, list plans, enroll, list subscriptions) and maps Maxio wire
/// models to the application's domain models.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly IMaxioApiClient _client;
    private readonly SubscriberLockRegistry _locks;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        SubscriberLockRegistry locks,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _locks = locks;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        return products
            .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
            .Select(MapPlan)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle ?? string.Empty);
        }

        // Validate the requested plan is one we actually offer (and get its display name for the result).
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        // Serialize concurrent subscribe attempts for the same user so a double-click can't race.
        using (await _locks.AcquireAsync(subscriber.Reference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Idempotency: if the user already has a live subscription to this plan, return it.
            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscriber {Reference} already has live subscription {SubscriptionId} to plan {Plan}; returning it.",
                    subscriber.Reference, existing.Id, plan.Handle);
                return MapSubscription(existing);
            }

            var created = await _client.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = _options.PaymentCollectionMethod,
                },
                cancellationToken);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} ({State}) for subscriber {Reference} on plan {Plan}.",
                created.Id, created.State, subscriber.Reference, plan.Handle);

            return MapSubscription(created);
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(MapSubscription)
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference,
                },
                cancellationToken);
        }
        catch (SubscriptionBillingException)
        {
            // The reference is unique in Maxio; a concurrent creator (in another process) or a
            // validation on the reference can cause this. Re-look up before giving up.
            var afterConflict = await _client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (afterConflict is not null)
            {
                return afterConflict;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            SubscriptionState.IsLive(s.State) &&
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = string.IsNullOrWhiteSpace(product.IntervalUnit) ? "month" : product.IntervalUnit!,
        RequiresPaymentMethod = product.RequireCreditCard,
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = string.IsNullOrWhiteSpace(subscription.State) ? "unknown" : subscription.State!,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
    };
}
