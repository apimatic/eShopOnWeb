using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed record SubscribeResult(BillingSubscription Subscription, bool Created);

public interface ISubscriptionBillingService
{
    Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="userName"/> and subscribes it to <paramref name="planHandle"/>.
    /// Idempotent: one customer per user and one subscription per user per plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}

/// <summary>
/// Orchestrates the subscribe flow. Every provider write follows claim, then SDK call, then record:
/// the claim is a primary-key insert into this application's own store, made before the write so that a
/// second caller is refused by the store before it reaches the provider.
/// </summary>
public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// A Pending claim untouched for longer than this was abandoned mid-write (the request budget plus the
    /// settle budget is far shorter); the next request for the same reference settles it.
    /// </summary>
    public static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);

    private readonly IMaxioBillingGateway _gateway;
    private readonly IBillingClaimStore _claims;
    private readonly IShopperProfileProvider _profiles;
    private readonly TimeProvider _clock;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        IMaxioBillingGateway gateway,
        IBillingClaimStore claims,
        IShopperProfileProvider profiles,
        TimeProvider clock,
        ILogger<SubscriptionBillingService> logger)
    {
        _gateway = gateway;
        _claims = claims;
        _profiles = profiles;
        _clock = clock;
        _logger = logger;
    }

    public Task<PlanCatalog> ListPlansAsync(CancellationToken cancellationToken) =>
        _gateway.ListPlansAsync(cancellationToken);

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        // The customer id comes only from the caller's own reference, never from request input.
        var customerReference = BillingReferences.ForCustomer(userName);
        var customer = await _gateway.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<BillingSubscription>();
        }
        return await _gateway.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new InvalidSubscriptionRequestException("planHandle is required.");
        }

        // Cross-operation invariant: the handle must be one the configured family's plan list returned.
        var catalog = await _gateway.ListPlansAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (plan == null)
        {
            if (catalog.Truncated)
            {
                throw new BillingProviderException(BillingFailureKind.CatalogIncomplete,
                    "The plan list could not be read completely, so this plan cannot be validated right now.");
            }
            throw new InvalidSubscriptionRequestException("planHandle is not an available subscription plan.");
        }

        var customerReference = BillingReferences.ForCustomer(userName);
        var customerId = await EnsureCustomerAsync(userName, customerReference, cancellationToken);

        var subscriptionReference = BillingReferences.ForSubscription(customerReference, plan.Handle);
        return await EnsureSubscriptionAsync(customerReference, customerId, plan, subscriptionReference, cancellationToken);
    }

    // ---------------------------------------------------------------- customer

    private async Task<int> EnsureCustomerAsync(string userName, string customerReference, CancellationToken cancellationToken)
    {
        var hold = await AcquireAsync(new BillingCustomerClaim(customerReference, _clock.GetUtcNow()), cancellationToken);
        if (hold.IsCompleted)
        {
            return hold.Claim.ProviderId!.Value;
        }
        var claim = hold.Claim;

        // Settle before writing: a customer may already exist for this reference (earlier run, unknown outcome).
        BillingCustomer? existing;
        ShopperProfile? profile;
        try
        {
            existing = await _gateway.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            profile = existing == null ? await _profiles.GetAsync(userName) : null;
        }
        catch (Exception)
        {
            await AbandonAsync(hold);
            throw;
        }

        if (existing != null)
        {
            await CompleteAsync(claim, existing.Id);
            return existing.Id;
        }

        if (profile == null)
        {
            await AbandonAsync(hold);
            throw new InvalidSubscriptionRequestException("Your account has no e-mail address, which billing requires.");
        }

        WriteOutcome<BillingCustomer> outcome;
        try
        {
            outcome = await _gateway.CreateCustomerAsync(
                new NewBillingCustomer(customerReference, profile.FirstName, profile.LastName, profile.Email), cancellationToken);
        }
        catch (BillingProviderException)
        {
            // Definitive non-acceptance (our credentials/configuration/throttling): nothing was created.
            await AbandonAsync(hold);
            throw;
        }

        switch (outcome.Kind)
        {
            case WriteOutcomeKind.Created:
            case WriteOutcomeKind.AlreadyExisted:
                await CompleteAsync(claim, outcome.Value!.Id);
                return outcome.Value.Id;

            case WriteOutcomeKind.Refused:
                await _claims.ReleaseAsync(claim, CancellationToken.None);
                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "The billing provider refused to create the billing account.", outcome.ProviderStatus, outcome.Messages);

            default:
                await MarkUnknownAsync(claim);
                throw new BillingOperationPendingException(BillingPendingKind.OutcomeUnknown, customerReference,
                    "Your billing account is being set up but the result is not confirmed yet. Retry the same request shortly.");
        }
    }

    // ---------------------------------------------------------------- subscription

    private async Task<SubscribeResult> EnsureSubscriptionAsync(
        string customerReference, int customerId, BillingPlan plan, string subscriptionReference, CancellationToken cancellationToken)
    {
        var hold = await AcquireAsync(
            new BillingSubscriptionClaim(subscriptionReference, customerReference, plan.Handle, _clock.GetUtcNow()), cancellationToken);
        var lookup = new SubscriptionLookup(subscriptionReference, customerId, plan.Handle, hold.PreviousAttemptStartedAt);

        if (hold.IsCompleted)
        {
            // Repeat subscribe: return the existing subscription with its current state.
            var current = await _gateway.FindSubscriptionAsync(lookup, cancellationToken)
                ?? (await _gateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken))
                    .FirstOrDefault(s => s.Id == hold.Claim.ProviderId);
            if (current == null)
            {
                throw new BillingProviderException(BillingFailureKind.Unavailable,
                    "The recorded subscription could not be read from the billing provider.");
            }
            return new SubscribeResult(current, Created: false);
        }
        var claim = hold.Claim;

        // Settle before writing: never send CreateSubscription for a reference the provider may already hold.
        BillingSubscription? existing;
        try
        {
            existing = await _gateway.FindSubscriptionAsync(lookup, cancellationToken);
        }
        catch (Exception)
        {
            await AbandonAsync(hold);
            throw;
        }
        if (existing != null)
        {
            await CompleteAsync(claim, existing.Id);
            // Found while settling an earlier unconfirmed attempt: that attempt created it.
            return new SubscribeResult(existing, Created: hold.IsTakeOver);
        }

        WriteOutcome<BillingSubscription> outcome;
        try
        {
            outcome = await _gateway.CreateSubscriptionAsync(
                new NewBillingSubscription(subscriptionReference, customerId, plan.Handle, claim.AttemptStartedAt), cancellationToken);
        }
        catch (BillingProviderException)
        {
            await AbandonAsync(hold);
            throw;
        }

        switch (outcome.Kind)
        {
            case WriteOutcomeKind.Created:
                await CompleteAsync(claim, outcome.Value!.Id);
                return new SubscribeResult(outcome.Value, Created: true);

            case WriteOutcomeKind.AlreadyExisted:
                await CompleteAsync(claim, outcome.Value!.Id);
                return new SubscribeResult(outcome.Value, Created: false);

            case WriteOutcomeKind.Refused:
                await _claims.ReleaseAsync(claim, CancellationToken.None);
                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "The billing provider refused the subscription.", outcome.ProviderStatus, outcome.Messages);

            default:
                await MarkUnknownAsync(claim);
                throw new BillingOperationPendingException(BillingPendingKind.OutcomeUnknown, subscriptionReference,
                    "Your subscription request was sent but its result is not confirmed yet. Retry the same request shortly to see the outcome.");
        }
    }

    // ---------------------------------------------------------------- claims

    private sealed record ClaimHold<TClaim>(TClaim Claim, bool IsCompleted, bool IsTakeOver, DateTimeOffset? PreviousAttemptStartedAt)
        where TClaim : BillingClaim;

    /// <summary>
    /// Claim first. The primary-key insert is the guard; a refused insert is resolved from the existing claim:
    /// Created returns it, a live Pending is in flight (409), a stale Pending or Unknown is taken over (under
    /// optimistic concurrency, so only one caller wins) and settled by the caller before any write.
    /// </summary>
    private async Task<ClaimHold<TClaim>> AcquireAsync<TClaim>(TClaim candidate, CancellationToken cancellationToken)
        where TClaim : BillingClaim
    {
        if (await _claims.TryInsertAsync(candidate, cancellationToken))
        {
            _logger.LogInformation("Billing claim taken for {Reference}.", candidate.Reference);
            return new ClaimHold<TClaim>(candidate, IsCompleted: false, IsTakeOver: false, PreviousAttemptStartedAt: null);
        }

        var existing = await _claims.FindAsync<TClaim>(candidate.Reference, cancellationToken);
        if (existing == null)
        {
            // Released between the refused insert and this read: another request is mid-flight.
            throw InFlight(candidate.Reference);
        }
        if (existing.Status == BillingClaimStatus.Created && existing.ProviderId != null)
        {
            return new ClaimHold<TClaim>(existing, IsCompleted: true, IsTakeOver: false, PreviousAttemptStartedAt: null);
        }

        var now = _clock.GetUtcNow();
        if (existing.Status == BillingClaimStatus.Pending && !existing.IsStale(now, StaleClaimAfter))
        {
            throw InFlight(candidate.Reference);
        }

        var previousStatus = existing.Status;
        var previousAttempt = existing.AttemptStartedAt;
        existing.TakeOver(now);
        if (!await _claims.TryUpdateAsync(existing, cancellationToken))
        {
            throw InFlight(candidate.Reference);
        }
        _logger.LogWarning("Billing claim for {Reference} was {PreviousStatus}; settling it before any new write.",
            candidate.Reference, previousStatus);
        return new ClaimHold<TClaim>(existing, IsCompleted: false, IsTakeOver: true, PreviousAttemptStartedAt: previousAttempt);
    }

    /// <summary>Gives up a held claim when no write was sent (or the provider definitively did not accept it).</summary>
    private async Task AbandonAsync<TClaim>(ClaimHold<TClaim> hold) where TClaim : BillingClaim
    {
        if (hold.IsTakeOver)
        {
            // An earlier attempt is still unsettled: keep the claim, as Unknown, for the next request to settle.
            await MarkUnknownAsync(hold.Claim);
        }
        else
        {
            await _claims.ReleaseAsync(hold.Claim, CancellationToken.None);
        }
    }

    // Results are recorded with CancellationToken.None: a caller that disconnected must not stop the record.
    private async Task CompleteAsync(BillingClaim claim, int providerId)
    {
        claim.MarkCreated(providerId, _clock.GetUtcNow());
        if (!await _claims.TryUpdateAsync(claim, CancellationToken.None))
        {
            _logger.LogWarning("Billing claim for {Reference} changed concurrently while recording provider id {ProviderId}.",
                claim.Reference, providerId);
        }
    }

    private async Task MarkUnknownAsync(BillingClaim claim)
    {
        claim.MarkUnknown(_clock.GetUtcNow());
        if (!await _claims.TryUpdateAsync(claim, CancellationToken.None))
        {
            _logger.LogWarning("Billing claim for {Reference} changed concurrently while recording an unknown outcome.", claim.Reference);
        }
    }

    private static BillingOperationPendingException InFlight(string reference) =>
        new(BillingPendingKind.InFlight, reference,
            "An identical request is already in progress. Retry shortly.");
}
