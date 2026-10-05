using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Subscribes shoppers to recurring plans held in the billing system (Maxio).
/// Every billing write follows the same order: claim locally (a primary-key insert the store refuses for a
/// second caller) → call the billing system → record the result. A write whose outcome is unknown keeps its
/// claim and is settled by looking it up by the reference stored on the claim.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    /// <summary>Total time one API request may spend waiting on the billing system.</summary>
    public static readonly TimeSpan DefaultBillingBudget = TimeSpan.FromSeconds(25);

    /// <summary>A pending claim older than this has no live owner (it exceeds any request's budget) and may be taken over.</summary>
    public static readonly TimeSpan ClaimStaleAfter = TimeSpan.FromSeconds(60);

    private static readonly HashSet<string> s_endedStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired", "failed_to_create" };

    private readonly IBillingGateway _gateway;
    private readonly ISubscriptionStore _store;
    private readonly IShopperDirectory _shoppers;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _billingBudget;

    public SubscriptionService(IBillingGateway gateway, ISubscriptionStore store, IShopperDirectory shoppers,
        IAppLogger<SubscriptionService> logger)
        : this(gateway, store, shoppers, logger, TimeProvider.System, DefaultBillingBudget)
    {
    }

    public SubscriptionService(IBillingGateway gateway, ISubscriptionStore store, IShopperDirectory shoppers,
        IAppLogger<SubscriptionService> logger, TimeProvider clock, TimeSpan billingBudget)
    {
        _gateway = gateway;
        _store = store;
        _shoppers = shoppers;
        _logger = logger;
        _clock = clock;
        _billingBudget = billingBudget;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken) =>
        WithinBillingBudget(budget => _gateway.ListPlansAsync(budget), cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string? firstName, string? lastName,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        return WithinBillingBudget(async budget =>
        {
            var shopper = await ResolveShopperAsync(userName, budget);

            // Only a plan this application offers may be subscribed to.
            var catalog = await _gateway.ListPlansAsync(budget);
            var plan = catalog.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal))
                ?? throw new SubscriptionPlanNotFoundException(planHandle);

            var claim = await ClaimEnrollmentAsync(shopper, plan, budget);
            if (claim.Existing is not null)
            {
                return claim.Existing;
            }

            var enrollment = claim.Held!;
            int customerId;
            try
            {
                customerId = await EnsureCustomerAsync(shopper, firstName, lastName, budget);
            }
            catch (Exception ex) when (claim.IsFresh && ex is BillingProviderException or OperationCanceledException or SubscriptionInProgressException)
            {
                // Nothing was sent for this enrollment yet: free the claim so the shopper can retry.
                await _store.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
                throw;
            }

            BillingSubscription created;
            try
            {
                created = await _gateway.CreateSubscriptionAsync(
                    new NewBillingSubscription(customerId, plan.Handle, enrollment.BillingReference), budget);
            }
            catch (BillingProviderException ex) when (!ex.OutcomeUnknown)
            {
                // The billing system refused the create, so nothing was written: free the claim.
                _logger.LogWarning("Maxio refused subscription {Reference} for user {UserId} (status {Status}).",
                    enrollment.BillingReference, shopper.UserId, ex.ProviderStatusCode?.ToString() ?? "none");
                await _store.ReleaseEnrollmentAsync(enrollment, CancellationToken.None);
                throw;
            }
            catch (BillingProviderException ex)
            {
                created = await SettleUnknownSubscriptionAsync(enrollment, ex, budget);
            }
            // An OperationCanceledException (budget spent or caller gone) leaves the claim pending on purpose:
            // the create may have landed, and the next request for this enrollment settles it by reference.

            await CompleteAsync(enrollment, created);
            _logger.LogInformation("User {UserId} subscribed to {Plan}: Maxio subscription {SubscriptionId} is {State}.",
                shopper.UserId, plan.Handle, created.Id, created.State);
            return new SubscribeResult(created, AlreadySubscribed: false);
        }, cancellationToken);
    }

    public Task<MySubscriptions> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        return WithinBillingBudget(async budget =>
        {
            var shopper = await ResolveShopperAsync(userName, budget);

            var link = await _store.GetCustomerAsync(shopper.UserId, CancellationToken.None);
            var customerId = link?.BillingCustomerId;
            if (link is not null && customerId is null)
            {
                // A customer create whose outcome was lost: settle it by reference.
                var account = await _gateway.FindCustomerByReferenceAsync(link.BillingReference, budget);
                if (account is not null)
                {
                    link.Link(account.Id);
                    await _store.SaveCustomerAsync(link, CancellationToken.None);
                    customerId = account.Id;
                }
            }

            var subscriptions = customerId is int id
                ? (await _gateway.ListCustomerSubscriptionsAsync(id, budget)).ToList()
                : new List<BillingSubscription>();

            var pending = new List<PendingSubscription>();
            foreach (var enrollment in await _store.ListEnrollmentsAsync(shopper.UserId, CancellationToken.None))
            {
                if (enrollment.Status != EnrollmentStatus.Pending)
                {
                    continue;
                }

                var match = subscriptions.FirstOrDefault(s => s.Reference == enrollment.BillingReference);
                if (match is null && enrollment.IsStale(Now, ClaimStaleAfter))
                {
                    match = await _gateway.FindSubscriptionByReferenceAsync(enrollment.BillingReference, budget);
                    if (match is not null)
                    {
                        subscriptions.Add(match);
                    }
                }

                if (match is null)
                {
                    pending.Add(new PendingSubscription(enrollment.PlanHandle, enrollment.BillingReference, enrollment.ClaimedAt));
                }
                else if (enrollment.IsStale(Now, ClaimStaleAfter))
                {
                    // No live request owns this claim any more, so this read settles it.
                    await CompleteAsync(enrollment, match);
                }
            }

            return new MySubscriptions(subscriptions, pending);
        }, cancellationToken);
    }

    private async Task<EnrollmentClaim> ClaimEnrollmentAsync(Shopper shopper, SubscriptionPlan plan, CancellationToken budget)
    {
        var enrollmentId = SubscriptionEnrollment.KeyFor(shopper.UserId, plan.Handle);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            // Reading first only spares the common repeat-click a refused insert; the insert below is the guard.
            var existing = await _store.GetEnrollmentAsync(enrollmentId, CancellationToken.None);
            if (existing is null)
            {
                var claim = new SubscriptionEnrollment(shopper.UserId, plan.Handle, Now);
                if (await _store.TryClaimEnrollmentAsync(claim, CancellationToken.None))
                {
                    return new EnrollmentClaim(claim, IsFresh: true, Existing: null);
                }

                existing = await _store.GetEnrollmentAsync(enrollmentId, CancellationToken.None);
                if (existing is null)
                {
                    continue; // released between our insert and our read: claim again
                }
            }

            if (existing.Status == EnrollmentStatus.Completed)
            {
                var current = await _gateway.FindSubscriptionByReferenceAsync(existing.BillingReference, budget);
                if (current is not null && !s_endedStates.Contains(current.State))
                {
                    return new EnrollmentClaim(null, IsFresh: false, new SubscribeResult(current, AlreadySubscribed: true));
                }

                // The earlier subscription is gone or has ended: free the slot and claim a new one.
                await _store.ReleaseEnrollmentAsync(existing, CancellationToken.None);
                continue;
            }

            if (!existing.IsStale(Now, ClaimStaleAfter)
                || !await _store.TryRenewEnrollmentClaimAsync(existing, Now, CancellationToken.None))
            {
                throw new SubscriptionInProgressException(
                    "A subscription request for this plan is already in progress. Check your subscriptions shortly.");
            }

            // We took over a claim whose earlier create may have landed: settle it before sending anything.
            var landed = await _gateway.FindSubscriptionByReferenceAsync(existing.BillingReference, budget);
            if (landed is not null)
            {
                await CompleteAsync(existing, landed);
                return new EnrollmentClaim(null, IsFresh: false, new SubscribeResult(landed, AlreadySubscribed: true));
            }

            return new EnrollmentClaim(existing, IsFresh: false, Existing: null);
        }

        throw new SubscriptionInProgressException(
            "A subscription request for this plan is already in progress. Check your subscriptions shortly.");
    }

    private async Task<BillingSubscription> SettleUnknownSubscriptionAsync(SubscriptionEnrollment enrollment,
        BillingProviderException failure, CancellationToken budget)
    {
        _logger.LogWarning("Outcome of Maxio subscription {Reference} is unknown ({Kind}); looking it up by reference.",
            enrollment.BillingReference, failure.Kind.ToString());

        BillingSubscription? found = null;
        try
        {
            found = await _gateway.FindSubscriptionByReferenceAsync(enrollment.BillingReference, budget);
        }
        catch (Exception lookupFailure) when (lookupFailure is BillingProviderException or OperationCanceledException)
        {
            // Still unknown: the claim stays pending and is settled by the next request for it.
        }

        if (found is null)
        {
            _logger.LogWarning("Maxio subscription {Reference} could not be confirmed; its claim stays pending for reconciliation.",
                enrollment.BillingReference);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return found!;
    }

    private async Task<int> EnsureCustomerAsync(Shopper shopper, string? firstName, string? lastName, CancellationToken budget)
    {
        var link = await _store.GetCustomerAsync(shopper.UserId, CancellationToken.None);
        if (link?.BillingCustomerId is int linkedId)
        {
            return linkedId;
        }

        BillingCustomer claim;
        var fresh = false;
        if (link is null)
        {
            claim = new BillingCustomer(shopper.UserId, Now);
            fresh = await _store.TryClaimCustomerAsync(claim, CancellationToken.None);
            if (!fresh)
            {
                link = await _store.GetCustomerAsync(shopper.UserId, CancellationToken.None);
                if (link?.BillingCustomerId is int raceWinnerId)
                {
                    return raceWinnerId;
                }

                claim = await TakeOverCustomerClaimAsync(link);
            }
        }
        else
        {
            claim = await TakeOverCustomerClaimAsync(link);
        }

        var createSent = false;
        try
        {
            // Look up first: an earlier attempt may already have created the customer.
            var account = await _gateway.FindCustomerByReferenceAsync(claim.BillingReference, budget);
            if (account is null)
            {
                createSent = true;
                account = await CreateCustomerAsync(claim, shopper, firstName, lastName, budget);
            }

            claim.Link(account.Id);
            await _store.SaveCustomerAsync(claim, CancellationToken.None);
            return account.Id;
        }
        catch (Exception ex) when (fresh && !createSent && ex is BillingProviderException or OperationCanceledException)
        {
            await _store.ReleaseCustomerClaimAsync(claim, CancellationToken.None);
            throw;
        }
    }

    private async Task<BillingCustomer> TakeOverCustomerClaimAsync(BillingCustomer? link)
    {
        if (link is null || !link.IsStale(Now, ClaimStaleAfter)
            || !await _store.TryRenewCustomerClaimAsync(link, Now, CancellationToken.None))
        {
            throw new SubscriptionInProgressException(
                "Your billing account is being set up by another request. Try again shortly.");
        }

        return link;
    }

    private async Task<BillingCustomerAccount> CreateCustomerAsync(BillingCustomer claim, Shopper shopper,
        string? firstName, string? lastName, CancellationToken budget)
    {
        var request = new NewBillingCustomer(
            claim.BillingReference,
            string.IsNullOrWhiteSpace(firstName) ? DefaultFirstName(shopper.Email) : firstName.Trim(),
            string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName.Trim(),
            shopper.Email);

        try
        {
            return await _gateway.CreateCustomerAsync(request, budget);
        }
        catch (BillingProviderException ex) when (ex.Kind == BillingFailureKind.Rejected)
        {
            // Maxio allows one customer per reference: a rejection may mean an earlier attempt already created it.
            var existing = await _gateway.FindCustomerByReferenceAsync(claim.BillingReference, budget);
            if (existing is not null)
            {
                return existing;
            }

            await _store.ReleaseCustomerClaimAsync(claim, CancellationToken.None);
            throw;
        }
        catch (BillingProviderException ex) when (!ex.OutcomeUnknown)
        {
            await _store.ReleaseCustomerClaimAsync(claim, CancellationToken.None);
            throw;
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Outcome of Maxio customer create for user {UserId} is unknown ({Kind}); looking it up by reference.",
                shopper.UserId, ex.Kind.ToString());
            BillingCustomerAccount? existing = null;
            try
            {
                existing = await _gateway.FindCustomerByReferenceAsync(claim.BillingReference, budget);
            }
            catch (Exception lookupFailure) when (lookupFailure is BillingProviderException or OperationCanceledException)
            {
                // Still unknown: the claim stays pending and the next attempt looks the customer up first.
            }

            if (existing is null)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
            }

            return existing!;
        }
    }

    private static string DefaultFirstName(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email[..at] : email;
    }

    private async Task CompleteAsync(SubscriptionEnrollment enrollment, BillingSubscription subscription)
    {
        enrollment.MarkCompleted(subscription.Id, Now);
        if (!await _store.SaveEnrollmentAsync(enrollment, CancellationToken.None))
        {
            // Another request took the claim over; it settles the same subscription by the same reference.
            _logger.LogWarning("Enrollment {EnrollmentId} changed concurrently; completion left to its current owner.", enrollment.Id);
        }
    }

    private async Task<Shopper> ResolveShopperAsync(string userName, CancellationToken cancellationToken) =>
        await _shoppers.FindByUserNameAsync(userName, cancellationToken) ?? throw new ShopperNotFoundException();

    /// <summary>
    /// Runs all billing work of one API request under a single deadline, so a slow or unresponsive billing
    /// system can never hold the caller longer than the budget, however many calls the request makes.
    /// </summary>
    private async Task<T> WithinBillingBudget<T>(Func<CancellationToken, Task<T>> work, CancellationToken requestAborted)
    {
        using var deadline = new CancellationTokenSource(_billingBudget, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, deadline.Token);
        try
        {
            return await work(linked.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !requestAborted.IsCancellationRequested)
        {
            throw BillingProviderException.NoResponse(
                $"Maxio did not respond within {_billingBudget.TotalSeconds:0} seconds.", innerException: ex);
        }
    }

    private sealed record EnrollmentClaim(SubscriptionEnrollment? Held, bool IsFresh, SubscribeResult? Existing);
}
