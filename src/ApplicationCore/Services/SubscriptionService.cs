using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Subscribes buyers to plans held in the billing system of record.
/// Every billing write follows claim → provider call → record: the claim is a row the store refuses to insert
/// twice, so a double-submit is stopped before it reaches the provider. A write whose outcome is unknown is
/// settled by looking it up by the reference this service generated and stored on the claim.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>
    /// A claim older than this cannot still be in flight: a subscribe makes at most five bounded provider calls.
    /// </summary>
    public static readonly TimeSpan ClaimStaleAfter = TimeSpan.FromMinutes(2);

    private const string SubscriptionReferencePrefix = "eshop-sub-";
    private const int MaxClaimAttempts = 2;

    // Subscriptions in these states are over; holding one does not stop the buyer subscribing to the plan again.
    private static readonly HashSet<string> EndedStates = new(StringComparer.Ordinal) { "canceled", "expired", "failed_to_create" };

    private readonly ISubscriptionBillingGateway _gateway;
    private readonly ISubscriptionClaimStore _store;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly TimeProvider _timeProvider;

    public SubscriptionService(ISubscriptionBillingGateway gateway,
        ISubscriptionClaimStore store,
        IAppLogger<SubscriptionService> logger,
        TimeProvider timeProvider)
    {
        _gateway = gateway;
        _store = store;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    public Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(command, nameof(command));
        Guard.Against.NullOrWhiteSpace(command.BuyerId, nameof(command.BuyerId));
        Guard.Against.NullOrWhiteSpace(command.Email, nameof(command.Email));
        Guard.Against.NullOrWhiteSpace(command.PlanHandle, nameof(command.PlanHandle));

        // The plan must be one this storefront offers: the configured family's catalog defines the legal set.
        var catalog = await _gateway.ListPlansAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, command.PlanHandle, StringComparison.Ordinal))
            ?? throw new SubscriptionPlanNotFoundException(command.PlanHandle);

        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            var claim = new SubscriptionEnrollment(command.BuyerId, plan.Handle, NewSubscriptionReference(), Now);
            if (await _store.TryClaimEnrollmentAsync(claim, cancellationToken))
            {
                return await EnrollAsync(claim, command, cancellationToken);
            }

            var existing = await _store.GetEnrollmentAsync(command.BuyerId, plan.Handle, cancellationToken);
            if (existing is null)
            {
                continue; // released between our claim and this read — claim again
            }

            var resolved = await ResolveExistingAsync(existing, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
            // the existing claim was released (the provider holds no live subscription for it) — claim again
        }

        throw new SubscriptionInProgressException("A subscription request for this plan is already being processed. Retry shortly.");
    }

    public async Task<MySubscriptions> GetMySubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));

        var pending = new List<PendingEnrollment>();
        var enrollments = await _store.ListEnrollmentsAsync(buyerId, cancellationToken);
        foreach (var enrollment in enrollments.Where(e => e.Status != EnrollmentStatus.Active))
        {
            if (enrollment.Status == EnrollmentStatus.Pending && !enrollment.IsStale(Now, ClaimStaleAfter))
            {
                pending.Add(new PendingEnrollment(enrollment.PlanHandle, enrollment.SubscriptionReference));
                continue;
            }

            var settled = await SettleAsync(enrollment, SubscribeOutcome.Existing, cancellationToken);
            if (settled?.Outcome == SubscribeOutcome.Pending)
            {
                pending.Add(new PendingEnrollment(enrollment.PlanHandle, enrollment.SubscriptionReference));
            }
        }

        var customer = await _store.GetCustomerAsync(buyerId, cancellationToken);
        if (customer?.BillingCustomerId is not int billingCustomerId)
        {
            return new MySubscriptions(Array.Empty<BillingSubscription>(), pending);
        }

        var subscriptions = await _gateway.ListCustomerSubscriptionsAsync(billingCustomerId, cancellationToken);
        return new MySubscriptions(subscriptions.OrderByDescending(s => s.CreatedAt).ToList(), pending);
    }

    private async Task<SubscribeResult> EnrollAsync(SubscriptionEnrollment claim, SubscribeCommand command, CancellationToken cancellationToken)
    {
        int billingCustomerId;
        try
        {
            billingCustomerId = await EnsureCustomerAsync(command, cancellationToken);
        }
        catch
        {
            // nothing was sent for the subscription yet — release so the buyer can try again
            await _store.ReleaseEnrollmentAsync(claim, CancellationToken.None);
            throw;
        }

        return await CreateAndRecordAsync(claim, billingCustomerId);
    }

    private async Task<SubscribeResult> CreateAndRecordAsync(SubscriptionEnrollment claim, int billingCustomerId)
    {
        // A claimed write runs to a settled outcome even if the caller disconnects, so it is not tied to the request token.
        BillingSubscription created;
        try
        {
            created = await _gateway.CreateSubscriptionAsync(
                new NewBillingSubscription(claim.PlanHandle, billingCustomerId, claim.SubscriptionReference),
                CancellationToken.None);
        }
        catch (BillingOutcomeUnknownException ex)
        {
            _logger.LogError(ex, "Subscription create for buyer {BuyerId}, plan {PlanHandle}, reference {SubscriptionReference} has an unknown outcome; settling by reference lookup.",
                claim.BuyerId, claim.PlanHandle, claim.SubscriptionReference);
            return await SettleAsync(claim, SubscribeOutcome.Created, CancellationToken.None)
                ?? throw new BillingProviderUnavailableException("The billing system did not confirm the subscription. Please retry.", ex.ProviderStatusCode, ex);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Subscription create for buyer {BuyerId}, plan {PlanHandle}, reference {SubscriptionReference} was refused (provider status {ProviderStatus}); releasing the claim.",
                claim.BuyerId, claim.PlanHandle, claim.SubscriptionReference, ex.ProviderStatusCode?.ToString() ?? "none");
            await _store.ReleaseEnrollmentAsync(claim, CancellationToken.None);
            throw;
        }

        claim.MarkActive(created.Id, Now);
        try
        {
            await _store.UpdateEnrollmentAsync(claim, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The subscription exists; the claim stays Pending and is settled by reference once it is stale.
            _logger.LogError(ex, "Subscription {SubscriptionId} (reference {SubscriptionReference}) was created but could not be recorded locally.",
                created.Id, claim.SubscriptionReference);
        }

        _logger.LogInformation("Buyer {BuyerId} subscribed to plan {PlanHandle}: subscription {SubscriptionId}, reference {SubscriptionReference}, state {State}.",
            claim.BuyerId, claim.PlanHandle, created.Id, claim.SubscriptionReference, created.State);
        return new SubscribeResult(SubscribeOutcome.Created, claim.SubscriptionReference, created);
    }

    /// <returns>The resolved result, or null when the existing claim was released and the caller may claim again.</returns>
    private async Task<SubscribeResult?> ResolveExistingAsync(SubscriptionEnrollment existing, CancellationToken cancellationToken)
    {
        switch (existing.Status)
        {
            case EnrollmentStatus.Active:
                var subscription = await _gateway.FindSubscriptionByReferenceAsync(existing.SubscriptionReference, cancellationToken);
                if (subscription is not null && !EndedStates.Contains(subscription.State))
                {
                    return new SubscribeResult(SubscribeOutcome.Existing, existing.SubscriptionReference, subscription);
                }

                _logger.LogInformation("Enrollment {SubscriptionReference} for buyer {BuyerId}, plan {PlanHandle} has no live subscription ({State}); releasing it.",
                    existing.SubscriptionReference, existing.BuyerId, existing.PlanHandle, subscription?.State ?? "not found");
                await _store.ReleaseEnrollmentAsync(existing, CancellationToken.None);
                return null;

            case EnrollmentStatus.Pending when !existing.IsStale(Now, ClaimStaleAfter):
                throw new SubscriptionInProgressException("A subscription request for this plan is already being processed. Retry shortly.");

            default:
                return await SettleAsync(existing, SubscribeOutcome.Existing, cancellationToken);
        }
    }

    /// <summary>
    /// Settles an enrollment whose create call may or may not have landed, by looking up the reference it was sent with.
    /// </summary>
    /// <returns>
    /// The subscription when found (<paramref name="whenFound"/>); a Pending result when the outcome is still unknown;
    /// null when the claim was stale, the provider holds nothing for it, and it was released.
    /// </returns>
    private async Task<SubscribeResult?> SettleAsync(SubscriptionEnrollment enrollment, SubscribeOutcome whenFound, CancellationToken cancellationToken)
    {
        BillingSubscription? found;
        try
        {
            found = await _gateway.FindSubscriptionByReferenceAsync(enrollment.SubscriptionReference, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogError(ex, "Could not settle enrollment {SubscriptionReference} for buyer {BuyerId}; it stays unknown.",
                enrollment.SubscriptionReference, enrollment.BuyerId);
            await MarkUnknownAsync(enrollment);
            return new SubscribeResult(SubscribeOutcome.Pending, enrollment.SubscriptionReference, null);
        }

        if (found is not null)
        {
            enrollment.MarkActive(found.Id, Now);
            await _store.UpdateEnrollmentAsync(enrollment, CancellationToken.None);
            _logger.LogInformation("Enrollment {SubscriptionReference} for buyer {BuyerId} settled: subscription {SubscriptionId} exists.",
                enrollment.SubscriptionReference, enrollment.BuyerId, found.Id);
            return new SubscribeResult(whenFound, enrollment.SubscriptionReference, found);
        }

        if (enrollment.IsStale(Now, ClaimStaleAfter))
        {
            _logger.LogWarning("Enrollment {SubscriptionReference} for buyer {BuyerId} settled: no subscription was created; releasing the claim.",
                enrollment.SubscriptionReference, enrollment.BuyerId);
            await _store.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
            return null;
        }

        // Not found yet, but too recent to rule out a create that is still becoming visible.
        await MarkUnknownAsync(enrollment);
        return new SubscribeResult(SubscribeOutcome.Pending, enrollment.SubscriptionReference, null);
    }

    private async Task MarkUnknownAsync(SubscriptionEnrollment enrollment)
    {
        if (enrollment.Status == EnrollmentStatus.Unknown)
        {
            return;
        }

        enrollment.MarkUnknown(Now);
        await _store.UpdateEnrollmentAsync(enrollment, CancellationToken.None);
    }

    private async Task<int> EnsureCustomerAsync(SubscribeCommand command, CancellationToken cancellationToken)
    {
        var record = await _store.GetCustomerAsync(command.BuyerId, cancellationToken);
        if (record?.BillingCustomerId is int knownId)
        {
            return knownId;
        }

        if (record is null)
        {
            record = new BillingCustomer(command.BuyerId, Now);
            if (!await _store.TryClaimCustomerAsync(record, cancellationToken))
            {
                record = await _store.GetCustomerAsync(command.BuyerId, cancellationToken)
                    ?? throw new SubscriptionInProgressException("Your billing account is being set up. Retry shortly.");
                if (record.BillingCustomerId is int recordedId)
                {
                    return recordedId;
                }

                if (!record.IsStale(Now, ClaimStaleAfter))
                {
                    return await AdoptInFlightCustomerAsync(record, cancellationToken);
                }
            }
        }
        else if (!record.IsStale(Now, ClaimStaleAfter))
        {
            return await AdoptInFlightCustomerAsync(record, cancellationToken);
        }

        // This request owns the claim (fresh, or taken over from a stale one).
        try
        {
            var customer = await _gateway.FindCustomerByReferenceAsync(record.CustomerReference, cancellationToken)
                ?? await CreateCustomerAsync(record, command);
            record.RecordBillingCustomer(customer.Id);
            await _store.UpdateCustomerAsync(record, CancellationToken.None);
            return customer.Id;
        }
        catch
        {
            // Release so the next attempt claims afresh; it looks the reference up before creating anything.
            await _store.ReleaseCustomerAsync(record, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Another request holds the customer claim: use its customer if it exists already, otherwise ask the caller to retry.</summary>
    private async Task<int> AdoptInFlightCustomerAsync(BillingCustomer record, CancellationToken cancellationToken)
    {
        var existing = await _gateway.FindCustomerByReferenceAsync(record.CustomerReference, cancellationToken)
            ?? throw new SubscriptionInProgressException("Your billing account is being set up. Retry shortly.");
        return existing.Id;
    }

    private async Task<BillingCustomerRecord> CreateCustomerAsync(BillingCustomer record, SubscribeCommand command)
    {
        var newCustomer = new NewBillingCustomer(record.CustomerReference, command.Email, FirstNameFor(command), LastNameFor(command));
        try
        {
            return await _gateway.CreateCustomerAsync(newCustomer, CancellationToken.None);
        }
        catch (BillingOutcomeUnknownException ex)
        {
            _logger.LogError(ex, "Customer create for buyer {BuyerId} (reference {CustomerReference}) has an unknown outcome; settling by reference lookup.",
                record.BuyerId, record.CustomerReference);
            return await _gateway.FindCustomerByReferenceAsync(record.CustomerReference, CancellationToken.None)
                ?? throw new BillingProviderUnavailableException("The billing system did not confirm the customer account. Please retry.", ex.ProviderStatusCode, ex);
        }
        catch (BillingRequestRejectedException)
        {
            // The reference is unique per billing site: a concurrent creator may already hold it.
            var raced = await _gateway.FindCustomerByReferenceAsync(record.CustomerReference, CancellationToken.None);
            if (raced is null)
            {
                throw;
            }

            return raced;
        }
    }

    private static string FirstNameFor(SubscribeCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.FirstName))
        {
            return command.FirstName.Trim();
        }

        var at = command.Email.IndexOf('@');
        return at > 0 ? command.Email[..at] : command.Email;
    }

    private static string LastNameFor(SubscribeCommand command) =>
        string.IsNullOrWhiteSpace(command.LastName) ? "Customer" : command.LastName.Trim();

    private static string NewSubscriptionReference() => SubscriptionReferencePrefix + Guid.NewGuid().ToString("N");
}
