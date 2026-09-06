using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Enrolls shoppers in recurring plans, keeping the billing system as the single source of truth:
/// nothing about a subscription is stored locally, every read goes back to the gateway.
/// </summary>
/// <remarks>
/// Subscribing is idempotent through three layers, because none of them is sufficient alone:
/// <list type="number">
/// <item>a per-shopper lock, which serialises concurrent attempts inside this process;</item>
/// <item>a check for an existing enrollment, which catches attempts this process did not serialise
/// and repeats arriving long after the first one;</item>
/// <item>a uniqueness token on the create call, which lets the billing system reject a request
/// whose response was lost in flight.</item>
/// </list>
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingGateway _billingGateway;
    private readonly ISubscriberLock _subscriberLock;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly string? _defaultPlanHandle;
    private readonly string? _paymentCollectionMethod;

    public SubscriptionService(IBillingGateway billingGateway,
        ISubscriberLock subscriberLock,
        ISubscriptionOptions options,
        IAppLogger<SubscriptionService> logger)
    {
        _billingGateway = billingGateway;
        _subscriberLock = subscriberLock;
        _logger = logger;
        _defaultPlanHandle = options.DefaultPlanHandle;
        _paymentCollectionMethod = options.PaymentCollectionMethod;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        _billingGateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber,
        string? planHandle = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        var plan = await ResolvePlanAsync(planHandle, cancellationToken);

        // Serialise this shopper's attempts so a double-click cannot get two creates past the
        // existing-enrollment check below. Other shoppers are unaffected.
        using var _ = await _subscriberLock.AcquireAsync(subscriber.CustomerReference, cancellationToken);

        var (customer, customerCreated) = await EnsureCustomerAsync(subscriber, cancellationToken);

        var existing = await FindCurrentEnrollmentAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                $"Shopper {subscriber.BuyerId} is already enrolled in plan '{plan.Handle}' as subscription {existing.Id} ({existing.State}); not creating another.");
            return SubscribeResult.AlreadySubscribed(existing);
        }

        var request = new NewSubscriptionRequest(
            customer.Id,
            plan.Handle,
            plan.PricePointHandle,
            BuildSubscriptionReference(subscriber),
            // Without a caller-supplied key each attempt gets its own token, so a genuine retry
            // after a validation failure is not locked out by the billing system's dedupe window.
            uniquenessToken: idempotencyKey ?? Guid.NewGuid().ToString("N"),
            _paymentCollectionMethod);

        try
        {
            var subscription = await _billingGateway.CreateSubscriptionAsync(request, cancellationToken);
            _logger.LogInformation(
                $"Subscribed shopper {subscriber.BuyerId} to plan '{plan.Handle}' as subscription {subscription.Id} ({subscription.State}).");
            return SubscribeResult.NewlySubscribed(subscription, customerCreated);
        }
        catch (DuplicateBillingSubmissionException)
        {
            // The billing system saw this token before. It will not tell us how the first request
            // ended, so read the customer's subscriptions back and let the facts decide.
            var recovered = await FindCurrentEnrollmentAsync(customer.Id, plan.Handle, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogInformation(
                    $"Duplicate subscribe for shopper {subscriber.BuyerId}; the first attempt had already created subscription {recovered.Id}.");
                return SubscribeResult.AlreadySubscribed(recovered);
            }

            throw new SubscriptionConflictException(
                $"A subscribe request for plan '{plan.Handle}' is already being processed for this account and has not completed. Retry shortly.");
        }
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        var customer = await _billingGateway.FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);

        // A shopper who has never subscribed has no billing customer, which is not an error.
        if (customer is null) return Array.Empty<CustomerSubscription>();

        var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.OrderByDescending(s => s.ActivatedAt ?? s.CurrentPeriodStartedAt).ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var handle = string.IsNullOrWhiteSpace(planHandle) ? _defaultPlanHandle : planHandle!.Trim();

        if (string.IsNullOrWhiteSpace(handle))
        {
            // No plan asked for and no default configured: pick the only plan on offer if there is
            // exactly one, otherwise make the caller choose.
            var plans = await _billingGateway.ListPlansAsync(cancellationToken);
            if (plans.Count == 1) return plans.Single();

            throw new SubscriptionPlanNotFoundException(
                plans.Count == 0 ? "(none available)" : "(unspecified - name a plan handle)");
        }

        return await _billingGateway.FindPlanAsync(handle!, cancellationToken)
               ?? throw new SubscriptionPlanNotFoundException(handle!);
    }

    private async Task<(BillingCustomer Customer, bool Created)> EnsureCustomerAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken)
    {
        var existing = await _billingGateway.FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);
        if (existing is not null) return (existing, false);

        var request = new NewCustomerRequest(subscriber.CustomerReference, subscriber.Email,
            subscriber.FirstName, subscriber.LastName);

        try
        {
            var created = await _billingGateway.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation($"Created billing customer {created.Id} for shopper {subscriber.BuyerId}.");
            return (created, true);
        }
        catch (DuplicateBillingReferenceException)
        {
            // Another request created the customer between our lookup and our create. That is the
            // outcome we wanted anyway, so read it back rather than failing.
            var raced = await _billingGateway.FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);
            if (raced is not null) return (raced, false);

            throw new BillingGatewayException(
                $"The billing customer reference '{subscriber.CustomerReference}' is already taken but the customer cannot be read back.");
        }
    }

    private async Task<CustomerSubscription?> FindCurrentEnrollmentAsync(int customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Where(s => string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            .Where(s => SubscriptionStates.IsCurrentEnrollment(s.State))
            // Prefer a live subscription over one in a problem state, then the most recent.
            .OrderByDescending(s => s.IsLive)
            .ThenByDescending(s => s.Id)
            .FirstOrDefault();
    }

    // Unique per attempt on purpose: the billing system requires subscription references to be
    // unique site-wide and a failed attempt still consumes the reference it was given.
    private static string BuildSubscriptionReference(SubscriberIdentity subscriber) =>
        $"{subscriber.CustomerReference}-{Guid.NewGuid():N}";
}
