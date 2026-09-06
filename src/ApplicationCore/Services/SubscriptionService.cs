using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe flow on top of the billing provider, which is the system of record:
/// eShopOnWeb stores no subscription state of its own.
/// </summary>
/// <remarks>
/// <para>Idempotency is layered, because a shopper double-clicking "Subscribe" must never end up
/// with two customers or two subscriptions:</para>
/// <list type="number">
/// <item>The customer reference is derived deterministically from the shopper email, so
/// "look up, then create" converges on one customer. A create that loses the race fails validation
/// and is resolved by re-reading the customer.</item>
/// <item>Requests for one shopper are serialised in-process, which collapses the common
/// double-click before it reaches the provider at all.</item>
/// <item>A shopper who already holds a current subscription for the plan gets that subscription
/// back rather than a second one.</item>
/// <item>Every subscription is created with a unique reference. The provider enforces uniqueness on
/// it, so two racing creates cannot both succeed; the loser re-reads by reference and returns the
/// winner's subscription. A caller-supplied idempotency key feeds that reference, which makes a
/// retried request safe even across processes.</item>
/// </list>
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    private const int MaxReferenceLength = 100;

    private readonly IBillingGateway _billingGateway;
    private readonly ISubscriberDirectory _subscriberDirectory;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly SubscriptionOptions _options;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IBillingGateway billingGateway,
        ISubscriberDirectory subscriberDirectory,
        KeyedAsyncLock subscriberLock,
        SubscriptionOptions options,
        IAppLogger<SubscriptionService> logger)
    {
        _billingGateway = billingGateway;
        _subscriberDirectory = subscriberDirectory;
        _subscriberLock = subscriberLock;
        _options = options;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingGateway.ListPlansAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var subscriber = await ResolveSubscriberAsync(userName, null, null, cancellationToken);

        var customer = await _billingGateway.FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);
        if (customer is null)
        {
            // The shopper has never subscribed, so no billing customer exists yet. That is not an error.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subscriber = await ResolveSubscriberAsync(request.UserName, request.FirstName, request.LastName, cancellationToken);
        var plan = await ResolvePlanAsync(request.PlanHandle, cancellationToken);

        using (await _subscriberLock.AcquireAsync(subscriber.CustomerReference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // An explicit idempotency key identifies one logical subscribe attempt. If we have
            // already acted on it, hand back exactly what that attempt produced.
            var keyedReference = request.IdempotencyKey is null
                ? null
                : BuildReference(subscriber.CustomerReference, plan.Handle, request.IdempotencyKey);

            if (keyedReference is not null)
            {
                var replay = await _billingGateway.FindSubscriptionByReferenceAsync(keyedReference, cancellationToken);
                if (replay is not null)
                {
                    _logger.LogInformation(
                        "Subscribe replayed for {0} on plan {1}: returning existing subscription {2}.",
                        subscriber.CustomerReference, plan.Handle, replay.Id);
                    return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, replay, customer);
                }
            }

            var alreadyHeld = await FindCurrentSubscriptionForPlanAsync(customer.Id, plan.Handle, cancellationToken);
            if (alreadyHeld is not null)
            {
                _logger.LogInformation(
                    "Subscribe skipped for {0}: subscription {1} is already {2} on plan {3}.",
                    subscriber.CustomerReference, alreadyHeld.Id, alreadyHeld.RawState, plan.Handle);
                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, alreadyHeld, customer);
            }

            var reference = keyedReference
                ?? BuildReference(subscriber.CustomerReference, plan.Handle, NewIdempotencySuffix());

            var collectionMethod = await ResolveCollectionMethodAsync(plan, cancellationToken);

            try
            {
                var created = await _billingGateway.CreateSubscriptionAsync(
                    new NewSubscription(customer.Id, plan.Handle, reference, collectionMethod), cancellationToken);

                _logger.LogInformation(
                    "Created subscription {0} for {1} on plan {2} (state {3}).",
                    created.Id, subscriber.CustomerReference, plan.Handle, created.RawState);

                return new SubscribeResult(SubscribeOutcome.Created, created, customer);
            }
            catch (BillingRequestRejectedException ex)
            {
                // The provider rejects a duplicate reference, so a concurrent request in another
                // process may have just created this very subscription. If it did, that is a
                // success for this caller too; if it did not, the rejection was about something
                // else and has to surface.
                var winner = await _billingGateway.FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (winner is null)
                {
                    throw;
                }

                _logger.LogWarning(
                    "Subscribe for {0} on plan {1} lost a race on reference {2}; returning subscription {3}. Provider said: {4}",
                    subscriber.CustomerReference, plan.Handle, reference, winner.Id, ex.Message);

                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, winner, customer);
            }
        }
    }

    private async Task<SubscriberIdentity> ResolveSubscriberAsync(string userName, string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        var contact = await _subscriberDirectory.FindByUserNameAsync(userName, cancellationToken)
            ?? throw new SubscriberNotFoundException(userName);

        var reference = SubscriberIdentity.BuildCustomerReference(_options.CustomerReferencePrefix, contact.Email);
        return new SubscriberIdentity(contact.UserName, contact.Email, reference, firstName, lastName);
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? requestedHandle, CancellationToken cancellationToken)
    {
        var handle = requestedHandle ?? _options.DefaultPlanHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingRequestRejectedException(
                "A plan handle is required. Choose one from GET /api/subscription-plans, or configure Maxio:DefaultPlanHandle.");
        }

        // Validating against the configured product family keeps a caller from subscribing to a
        // product that is not part of this catalog, and lets us report the price the shopper is
        // about to be charged before we call the provider.
        var plans = await _billingGateway.ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));

        return plan ?? throw new SubscriptionPlanNotFoundException(handle, plans.Select(p => p.Handle));
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _billingGateway.FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _billingGateway.CreateCustomerAsync(
                new NewBillingCustomer(subscriber.CustomerReference, subscriber.Email, subscriber.FirstName, subscriber.LastName),
                cancellationToken);

            _logger.LogInformation(
                "Created billing customer {0} for reference {1}.", created.Id, subscriber.CustomerReference);

            return created;
        }
        catch (BillingRequestRejectedException ex)
        {
            // Customer references are unique at the provider, so a concurrent request may have won.
            var winner = await _billingGateway.FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            _logger.LogWarning(
                "Billing customer for {0} was created concurrently; using customer {1}. Provider said: {2}",
                subscriber.CustomerReference, winner.Id, ex.Message);

            return winner;
        }
    }

    private async Task<CustomerSubscription?> FindCurrentSubscriptionForPlanAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _billingGateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Where(s => s.IsCurrent && string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private async Task<string?> ResolveCollectionMethodAsync(SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        if (plan.RequiresPaymentMethod)
        {
            // The plan wants a card on file, so leave the site default (typically automatic) alone.
            return null;
        }

        // The plan does not require a payment profile. Signing up with automatic collection would be
        // rejected for want of a card, so pick the invoicing method the site architecture supports.
        var site = await _billingGateway.GetSiteAsync(cancellationToken);
        return site.CollectionMethodWithoutPaymentProfile;
    }

    /// <summary>
    /// Builds the subscription reference. It is deterministic in its inputs so that the same logical
    /// request always addresses the same subscription, and slugged because the value shows up in the
    /// provider UI.
    /// </summary>
    internal static string BuildReference(string customerReference, string planHandle, string discriminator)
    {
        var raw = $"{customerReference}--{planHandle}--{discriminator}";
        var slug = Slugify(raw);

        if (slug.Length <= MaxReferenceLength)
        {
            return slug;
        }

        // Keep the tail: it carries the plan and the discriminator, which are what distinguish one
        // reference from another.
        return slug[^MaxReferenceLength..];
    }

    private static string NewIdempotencySuffix() => Guid.NewGuid().ToString("n")[..12];

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c) || c is '-' || c is '_')
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }
}
