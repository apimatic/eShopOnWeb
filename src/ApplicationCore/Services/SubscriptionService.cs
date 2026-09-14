using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe flow on top of <see cref="ISubscriptionBillingGateway"/>.
/// </summary>
/// <remarks>
/// There is deliberately no local table of subscriptions. The billing provider is the system of
/// record and the shopper is found there by a reference derived from their user name
/// (<see cref="BillingReferences"/>), so nothing has to be migrated, seeded or kept in sync - and
/// the mapping survives a restart even when eShopOnWeb runs on the in-memory database.
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Collect by invoice rather than by card. eShopOnWeb captures no payment details, so an
    /// automatic collection method would fail the signup charge for want of a payment profile.
    /// </summary>
    public const string DefaultPaymentCollectionMethod = "remittance";

    /// <summary>
    /// How long a subscribe attempt without a caller-supplied key stays recognisable as a retry.
    /// Long enough to cover a double-click and our own retry budget, short enough that a shopper
    /// who cancels is not locked out of re-subscribing.
    /// </summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(5);

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly KeyedAsyncLock _subscriberLocks;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly TimeProvider _timeProvider;

    public SubscriptionService(
        ISubscriptionBillingGateway gateway,
        KeyedAsyncLock subscriberLocks,
        IAppLogger<SubscriptionService> logger,
        TimeProvider timeProvider)
    {
        _gateway = gateway;
        _subscriberLocks = subscriberLocks;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        var customer = await _gateway.FindCustomerByReferenceAsync(subscriber.BillingReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _gateway.ListSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));
        Guard.Against.Null(request.Subscriber, nameof(request.Subscriber));
        Guard.Against.NullOrWhiteSpace(request.PlanHandle, nameof(request.PlanHandle));

        var planHandle = request.PlanHandle.Trim();
        var plan = await _gateway.FindPlanAsync(planHandle, cancellationToken)
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var reference = request.Subscriber.BillingReference;

        using (await _subscriberLocks.AcquireAsync(reference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(request.Subscriber, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    $"Subscriber {reference} is already on plan {plan.Handle} (subscription {existing.Id}, state {existing.State}); returning the existing enrollment.");
                return new SubscribeResult(existing, AlreadySubscribed: true);
            }

            var newSubscription = new NewSubscription
            {
                CustomerId = customer.Id,
                PlanHandle = plan.Handle,
                PaymentCollectionMethod = string.IsNullOrWhiteSpace(request.PaymentCollectionMethod)
                    ? DefaultPaymentCollectionMethod
                    : request.PaymentCollectionMethod!.Trim(),
                IdempotencyToken = BuildIdempotencyToken(reference, plan.Handle, request.IdempotencyKey)
            };

            try
            {
                var created = await _gateway.CreateSubscriptionAsync(newSubscription, cancellationToken);
                _logger.LogInformation(
                    $"Subscribed {reference} to plan {plan.Handle} (subscription {created.Id}, state {created.State}).");
                return new SubscribeResult(created, AlreadySubscribed: false);
            }
            catch (ConcurrentSubscribeException)
            {
                // The provider recognised our token, so an identical request reached it first -
                // from another instance, or from a retry of a call that timed out on our side.
                // Whatever it created is the enrollment we want.
                var winner = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
                if (winner is not null)
                {
                    return new SubscribeResult(winner, AlreadySubscribed: true);
                }

                throw;
            }
        }
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var reference = subscriber.BillingReference;

        var existing = await _gateway.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = CustomerNames.Resolve(subscriber.EmailAddress, subscriber.FirstName, subscriber.LastName);

        var created = await _gateway.CreateCustomerAsync(
            new NewBillingCustomer
            {
                Reference = reference,
                Email = subscriber.EmailAddress,
                FirstName = firstName,
                LastName = lastName
            },
            cancellationToken);

        _logger.LogInformation($"Created billing customer {created.Id} for subscriber {reference}.");

        return created;
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _gateway.ListSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions.FirstOrDefault(subscription =>
            subscription.IsLive &&
            string.Equals(subscription.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A fingerprint of "this shopper enrolling on this plan". Two requests that mean the same
    /// thing produce the same token, which is what lets the provider reject the replay.
    /// </summary>
    /// <remarks>
    /// Without a caller-supplied key the fingerprint is scoped to a <see cref="RetryWindow"/>-wide
    /// bucket. A fingerprint that never changed would keep the provider rejecting requests long
    /// after a shopper cancelled, locking them out of subscribing again.
    /// </remarks>
    private string BuildIdempotencyToken(string billingReference, string planHandle, string? idempotencyKey)
    {
        var scope = string.IsNullOrWhiteSpace(idempotencyKey)
            ? (_timeProvider.GetUtcNow().UtcTicks / RetryWindow.Ticks).ToString(CultureInfo.InvariantCulture)
            : idempotencyKey!.Trim();

        var payload = string.Format(
            CultureInfo.InvariantCulture,
            "eshoponweb|subscribe|{0}|{1}|{2}",
            billingReference,
            planHandle,
            scope);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
