using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Subscribe flow: validate the plan against the provider's catalog → claim (insert-or-fail) → ensure the
/// provider customer → create the subscription carrying the claim's reference → record the result.
/// Claims whose outcome is unknown stay <see cref="SubscriptionEnrollmentStatus.Pending"/> and are settled by
/// re-reading the provider by reference on the next subscribe or read.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    public const string CustomerReferencePrefix = "eshop-user-";
    private const string SubscriptionReferencePrefix = "eshop-sub-";

    /// <summary>A pending claim older than this cannot still have a call in flight (it far exceeds every call budget),
    /// so a new attempt may take it over.</summary>
    public static readonly TimeSpan PendingClaimTakeoverAfter = TimeSpan.FromMinutes(2);

    private static readonly string[] EndedStates = { "canceled", "expired", "failed_to_create" };

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionEnrollmentStore _store;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly TimeProvider _timeProvider;

    public SubscriptionService(ISubscriptionBillingGateway gateway, ISubscriptionEnrollmentStore store,
        IAppLogger<SubscriptionService> logger, TimeProvider timeProvider)
    {
        _gateway = gateway;
        _store = store;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default) =>
        _gateway.GetPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(subscriber.UserName, nameof(subscriber.UserName));
        Guard.Against.NullOrWhiteSpace(subscriber.Email, nameof(subscriber.Email));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // The plan must be one this application offers: a product of the configured family.
        var catalog = await _gateway.GetPlansAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.Ordinal))
            ?? throw new UnknownSubscriptionPlanException(planHandle);

        var customerReference = CustomerReferenceFor(subscriber.UserName);

        // Bounded: one retry after releasing an enrollment whose subscription has ended.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claim = new SubscriptionEnrollment(customerReference, _gateway.ProductFamilyHandle, subscriber.UserName,
                plan.Handle, NewSubscriptionReference(), _timeProvider.GetUtcNow());

            var existing = await _store.TryClaimAsync(claim, cancellationToken);
            if (existing is null)
            {
                return await EnrollAsync(claim, subscriber, plan, cancellationToken);
            }

            var resolved = await ResolveExistingAsync(existing, subscriber, plan, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
            // The existing enrollment was released (its subscription ended): claim again.
        }

        throw new SubscriptionConflictException("A subscription request for this account is already in progress. Try again shortly.");
    }

    public async Task<MySubscriptions> GetMySubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(subscriber.UserName, nameof(subscriber.UserName));

        var customerReference = CustomerReferenceFor(subscriber.UserName);
        var enrollment = await _store.GetAsync(
            SubscriptionEnrollment.CreateClaimKey(customerReference, _gateway.ProductFamilyHandle), cancellationToken);

        PendingEnrollment? pending = null;
        if (enrollment is { Status: SubscriptionEnrollmentStatus.Pending })
        {
            var settled = await TrySettleAsync(enrollment, cancellationToken);
            if (settled is null)
            {
                pending = new PendingEnrollment(enrollment.PlanHandle, enrollment.SubscriptionReference, enrollment.UpdatedAtUtc);
            }
        }

        // The provider is the system of record: the local row only caches the customer id.
        var customerId = enrollment?.BillingCustomerId ?? await _gateway.FindCustomerIdAsync(customerReference, cancellationToken);
        if (customerId is null)
        {
            return new MySubscriptions(Array.Empty<BillingSubscription>(), pending);
        }

        var mine = await ListFamilySubscriptionsAsync(customerId.Value, cancellationToken);
        return new MySubscriptions(mine, pending);
    }

    /// <summary>
    /// The billing customer's reference: deterministic per eShop user (so it survives a lost local store) but opaque,
    /// so no email address travels in provider URLs, references or our logs.
    /// </summary>
    public static string CustomerReferenceFor(string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToLowerInvariant()));
        return CustomerReferencePrefix + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    private static string NewSubscriptionReference() => SubscriptionReferencePrefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Handles a claim refused because an enrollment already exists. Returns null when that enrollment was
    /// released and the caller should claim again.
    /// </summary>
    private async Task<SubscribeResult?> ResolveExistingAsync(SubscriptionEnrollment existing, Subscriber subscriber,
        BillingPlan plan, CancellationToken cancellationToken)
    {
        BillingSubscription? current;
        if (existing.Status == SubscriptionEnrollmentStatus.Pending)
        {
            current = await TrySettleAsync(existing, cancellationToken);
            if (current is null)
            {
                if (!existing.IsPendingLongerThan(PendingClaimTakeoverAfter, _timeProvider.GetUtcNow()))
                {
                    throw new SubscriptionConflictException(
                        "A subscription request for this account is already in progress. Check your subscriptions shortly.");
                }

                // Stale and still not at the provider: take the claim over (the concurrency stamp lets only one caller win).
                existing.Retake(plan.Handle, _timeProvider.GetUtcNow());
                if (!await _store.TryUpdateAsync(existing, cancellationToken))
                {
                    throw new SubscriptionConflictException(
                        "A subscription request for this account is already in progress. Check your subscriptions shortly.");
                }
                _logger.LogWarning("Retrying stale pending subscription enrollment {SubscriptionReference} for {CustomerReference}",
                    existing.SubscriptionReference, existing.CustomerReference);
                return await EnrollAsync(existing, subscriber, plan, cancellationToken);
            }
        }
        else
        {
            current = existing.BillingSubscriptionId is int subscriptionId
                ? await _gateway.GetSubscriptionAsync(subscriptionId, cancellationToken)
                : await _gateway.FindSubscriptionAsync(existing.SubscriptionReference, cancellationToken);
        }

        if (current is null || EndedStates.Contains(current.State, StringComparer.Ordinal))
        {
            // The recorded subscription has ended (or no longer exists at the provider): free the slot for a new one.
            _logger.LogInformation("Releasing ended subscription enrollment {SubscriptionReference} for {CustomerReference}",
                existing.SubscriptionReference, existing.CustomerReference);
            await _store.ReleaseAsync(existing, cancellationToken);
            return null;
        }

        if (!string.Equals(current.PlanHandle ?? existing.PlanHandle, plan.Handle, StringComparison.Ordinal))
        {
            throw new SubscriptionConflictException(
                $"This account already has a '{current.PlanHandle ?? existing.PlanHandle}' subscription. Changing plans is not supported.");
        }

        return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, plan.Handle, existing.SubscriptionReference, current);
    }

    /// <summary>Runs the provider writes for a claim this caller holds.</summary>
    private async Task<SubscribeResult> EnrollAsync(SubscriptionEnrollment claim, Subscriber subscriber, BillingPlan plan,
        CancellationToken cancellationToken)
    {
        int customerId;
        BillingSubscription? live;
        try
        {
            var (firstName, lastName) = ResolveName(subscriber);
            customerId = await _gateway.EnsureCustomerAsync(
                new BillingCustomer(claim.CustomerReference, subscriber.Email, firstName, lastName), cancellationToken);

            claim.RecordCustomer(customerId, _timeProvider.GetUtcNow());
            if (!await _store.TryUpdateAsync(claim, cancellationToken))
            {
                throw new SubscriptionConflictException("A subscription request for this account is already in progress.");
            }

            // The provider is the system of record: if local state was lost (e.g. a restart of the in-memory store),
            // a live subscription may already exist there. Adopt it rather than create a second one.
            live = (await ListFamilySubscriptionsAsync(customerId, cancellationToken))
                .FirstOrDefault(s => !EndedStates.Contains(s.State, StringComparer.Ordinal));
        }
        catch (Exception)
        {
            // No subscription write has been made yet, so the claim can be released safely.
            await ReleaseQuietlyAsync(claim);
            throw;
        }

        if (live is not null)
        {
            claim.Adopt(customerId, live.Id, live.PlanHandle ?? plan.Handle, live.Reference, _timeProvider.GetUtcNow());
            await _store.TryUpdateAsync(claim, CancellationToken.None);
            _logger.LogInformation("Adopted existing subscription {SubscriptionId} for {CustomerReference}", live.Id, claim.CustomerReference);

            if (!string.Equals(claim.PlanHandle, plan.Handle, StringComparison.Ordinal))
            {
                throw new SubscriptionConflictException(
                    $"This account already has a '{claim.PlanHandle}' subscription. Changing plans is not supported.");
            }
            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, plan.Handle, claim.SubscriptionReference, live);
        }

        SubscriptionCreationResult result;
        try
        {
            result = await _gateway.CreateSubscriptionAsync(customerId, plan.Handle, claim.SubscriptionReference, cancellationToken);
        }
        catch (BillingProviderException)
        {
            // The gateway throws only for a definitive refusal (it has already checked the reference): nothing was created.
            await ReleaseQuietlyAsync(claim);
            throw;
        }
        // Any other exception (caller cancellation included) leaves the claim Pending: its outcome is settled by reference later.

        if (result.Subscription is null)
        {
            _logger.LogWarning("Subscription {SubscriptionReference} for {CustomerReference} has an unknown outcome; left pending for reconciliation",
                claim.SubscriptionReference, claim.CustomerReference);
            return new SubscribeResult(SubscribeOutcome.Pending, plan.Handle, claim.SubscriptionReference, null);
        }

        claim.MarkActive(customerId, result.Subscription.Id, result.Subscription.PlanHandle ?? plan.Handle, _timeProvider.GetUtcNow());
        // The provider has acted; the local record must not be lost to a caller that disconnected meanwhile.
        await _store.TryUpdateAsync(claim, CancellationToken.None);

        _logger.LogInformation("Created subscription {SubscriptionId} ({SubscriptionReference}) on plan {PlanHandle} for {CustomerReference}",
            result.Subscription.Id, claim.SubscriptionReference, plan.Handle, claim.CustomerReference);
        return new SubscribeResult(SubscribeOutcome.Created, plan.Handle, claim.SubscriptionReference, result.Subscription);
    }

    /// <summary>The customer's subscriptions to plans of the configured family, newest first.</summary>
    private async Task<IReadOnlyList<BillingSubscription>> ListFamilySubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        // When the provider omits the product family, fall back to the plan handle being one of the family's plans.
        HashSet<string>? planHandles = null;
        if (subscriptions.Any(s => s.ProductFamilyHandle is null))
        {
            var catalog = await _gateway.GetPlansAsync(cancellationToken);
            planHandles = catalog.Plans.Select(p => p.Handle).ToHashSet(StringComparer.Ordinal);
        }

        return subscriptions
            .Where(s => s.ProductFamilyHandle is not null
                ? string.Equals(s.ProductFamilyHandle, _gateway.ProductFamilyHandle, StringComparison.Ordinal)
                : s.PlanHandle is not null && planHandles!.Contains(s.PlanHandle))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    /// <summary>Re-reads the provider by the claim's reference and records the subscription when found.</summary>
    private async Task<BillingSubscription?> TrySettleAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        var found = await _gateway.FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
        if (found?.CustomerId is null)
        {
            return found;
        }

        enrollment.MarkActive(found.CustomerId.Value, found.Id, found.PlanHandle ?? enrollment.PlanHandle, _timeProvider.GetUtcNow());
        if (await _store.TryUpdateAsync(enrollment, CancellationToken.None))
        {
            _logger.LogInformation("Settled pending subscription enrollment {SubscriptionReference} as subscription {SubscriptionId}",
                enrollment.SubscriptionReference, found.Id);
        }
        return found;
    }

    private async Task ReleaseQuietlyAsync(SubscriptionEnrollment claim)
    {
        try
        {
            await _store.ReleaseAsync(claim, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A claim that cannot be released goes stale and is taken over after PendingClaimTakeoverAfter.
            _logger.LogWarning("Could not release subscription claim {SubscriptionReference}: {Error}",
                claim.SubscriptionReference, ex.GetType().Name);
        }
    }

    private static (string FirstName, string LastName) ResolveName(Subscriber subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) && !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (subscriber.FirstName.Trim(), subscriber.LastName.Trim());
        }

        // eShopOnWeb does not collect names; derive a readable one from the email's local part.
        var localPart = subscriber.Email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        var first = !string.IsNullOrWhiteSpace(subscriber.FirstName)
            ? subscriber.FirstName.Trim()
            : textInfo.ToTitleCase(parts.Length > 0 ? parts[0] : localPart);
        var last = !string.IsNullOrWhiteSpace(subscriber.LastName)
            ? subscriber.LastName.Trim()
            : parts.Length > 1 ? textInfo.ToTitleCase(string.Join(" ", parts.Skip(1))) : "Shopper";
        return (first, last);
    }
}
