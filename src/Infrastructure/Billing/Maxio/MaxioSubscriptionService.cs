using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Recipes;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscribe, list plans and list a shopper's subscriptions with Maxio as the system of record
/// (knowledge/lifecycles.md L2). Every signup follows one order: claim it in our own store, then
/// call Maxio, then record the result on the claim.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>Maxio works on a request for up to 120 s; an unknown outcome is not re-sent before then (R11).</summary>
    public static readonly TimeSpan UnknownOutcomeWindow = TimeSpan.FromSeconds(120);

    private const int MaxAttemptsPerCall = 3;

    private readonly CatalogContext _db;
    private readonly MaxioGateway _gateway;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _time;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(CatalogContext db, MaxioGateway gateway, IHostEnvironment environment,
        TimeProvider time, ILogger<MaxioSubscriptionService> logger)
    {
        _db = db;
        _gateway = gateway;
        _environment = environment;
        _time = time;
        _logger = logger;
    }

    // ================================================================= plans

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var site = await Guarded(() => _gateway.GetSiteAsync(cancellationToken));
        var plans = await Guarded(() => _gateway.ListPlansAsync(cancellationToken));
        return new SubscriptionPlanList(plans.Items.Select(p => ToPlan(p, site.Currency)).ToList(), plans.IsComplete);
    }

    // ================================================================= my subscriptions

    public async Task<CustomerSubscriptionList> ListSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        var pending = await _db.SubscriptionSignups.AsNoTracking()
            .Where(s => s.UserId == subscriber.UserId &&
                        (s.Status == SubscriptionSignupStatus.Pending ||
                         s.Status == SubscriptionSignupStatus.Unknown ||
                         s.Status == SubscriptionSignupStatus.ActionRequired))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
        var pendingSignups = pending
            .Select(s => new PendingSignup(s.Id, s.PlanHandle, s.Status.ToString(), s.ActionLink, s.CreatedAt))
            .ToList();

        var site = await Guarded(() => _gateway.GetSiteAsync(cancellationToken));
        var customer = await Guarded(() =>
            _gateway.FindCustomerAsync(SubscriptionSignup.CustomerReferenceFor(subscriber.UserId), cancellationToken));
        if (customer?.Id is not int customerId)
            return new CustomerSubscriptionList(Array.Empty<CustomerSubscription>(), pendingSignups, IsComplete: true);

        var subscriptions = await Guarded(() => _gateway.ListCustomerSubscriptionsAsync(customerId, cancellationToken));
        return new CustomerSubscriptionList(
            subscriptions.Items.Select(s => ToCustomerSubscription(s, site.Currency)).ToList(),
            pendingSignups,
            subscriptions.IsComplete);
    }

    // ================================================================= subscribe

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionBillingException(SubscriptionBillingError.UnknownPlan, "A plan handle is required.");
        planHandle = planHandle.Trim();

        // Cross-operation invariant: the handle must be one of the plans this shop offers.
        var site = await Guarded(() => _gateway.GetSiteAsync(cancellationToken));
        var plans = await Guarded(() => _gateway.ListPlansAsync(cancellationToken));
        var plan = plans.Items.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionBillingException(SubscriptionBillingError.UnknownPlan,
                $"'{planHandle}' is not a plan this shop offers.");
        }

        if (_environment.IsDevelopment() && !site.IsTest)
        {
            // Development traffic goes to a test site only (knowledge/rules.md R2).
            throw new SubscriptionBillingException(SubscriptionBillingError.WritesDisabled,
                "Subscribing is disabled in Development because the configured billing site is not a test site.");
        }

        for (var round = 0; round < MaxAttemptsPerCall; round++)
        {
            var signup = await ClaimAsync(subscriber, planHandle, cancellationToken);
            if (signup.Result is { } settled)
                return settled;

            var result = await RunSignupAsync(signup.Claim!, site, cancellationToken);
            if (result is not null)
                return result;
            // null: the subscription under this attempt's reference had ended; claim the next attempt.
        }

        throw new SubscriptionBillingException(SubscriptionBillingError.ProviderUnavailable,
            "The subscription could not be settled. Try again later.");
    }

    /// <summary>
    /// Re-runs an unsettled signup with its stored reference and tokens. Used by
    /// <see cref="SubscriptionSignupReconciler"/>; does nothing when another worker holds the lease.
    /// </summary>
    public async Task ResumeAsync(string signupId, CancellationToken cancellationToken)
    {
        var claim = await _db.SubscriptionSignups.SingleOrDefaultAsync(s => s.Id == signupId, cancellationToken);
        if (claim is null || !claim.IsOpen || claim.Status == SubscriptionSignupStatus.ActionRequired) return;
        if (!await TryLeaseAsync(claim, cancellationToken)) return;

        var site = await _gateway.GetSiteAsync(cancellationToken);
        await RunSignupAsync(claim, site, cancellationToken);
    }

    private sealed record ClaimResult(SubscriptionSignup? Claim, SubscribeResult? Result);

    /// <summary>
    /// The claim → (Maxio) order's first step. Either a leased claim to run, or an answer that needs no Maxio write.
    /// </summary>
    private async Task<ClaimResult> ClaimAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken)
    {
        var latest = await _db.SubscriptionSignups
            .Where(s => s.UserId == subscriber.UserId && s.PlanHandle == planHandle)
            .OrderByDescending(s => s.Attempt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is not null)
        {
            switch (latest.Status)
            {
                case SubscriptionSignupStatus.Completed:
                {
                    // Our record says it exists: confirm its standing from Maxio, the system of record.
                    var current = latest.BillingSubscriptionId is int id
                        ? await Guarded(() => _gateway.ReadSubscriptionAsync(id, cancellationToken))
                        : null;
                    if (current is not null && current.State.Standing() != SubscriptionStanding.Ended)
                        return new ClaimResult(null, Settled(SubscribeOutcome.AlreadySubscribed, latest, current));

                    // Ended (or gone): the user may subscribe to the plan again, under a new attempt.
                    latest.MarkEnded(Now);
                    await _db.SaveChangesAsync(cancellationToken);
                    break;
                }

                case SubscriptionSignupStatus.Pending:
                case SubscriptionSignupStatus.Unknown:
                case SubscriptionSignupStatus.ActionRequired:
                    if (!await TryLeaseAsync(latest, cancellationToken))
                        throw InProgress();
                    return new ClaimResult(latest, null);

                case SubscriptionSignupStatus.Rejected:
                case SubscriptionSignupStatus.Ended:
                    break;
            }
        }

        var claim = new SubscriptionSignup(subscriber.UserId, subscriber.Email, planHandle,
            (latest?.Attempt ?? 0) + 1, Now);
        _db.SubscriptionSignups.Add(claim);
        try
        {
            // The claim is a write of its own, before any Maxio write: a second click computes the same key.
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _db.Entry(claim).State = EntityState.Detached;
            _logger.LogInformation("Signup {SignupId} is already claimed by another request", claim.Id);
            throw InProgress();
        }

        _logger.LogInformation("Claimed signup {SignupId} (reference {Reference})", claim.Id, claim.SubscriptionReference);
        return new ClaimResult(claim, null);
    }

    /// <summary>
    /// Runs one leased claim: customer, held-plan check, safe signup, settle. Returns null when the
    /// subscription under this claim's reference had ended and a new attempt should be claimed.
    /// </summary>
    private async Task<SubscribeResult?> RunSignupAsync(SubscriptionSignup claim, MaxioSiteFacts site,
        CancellationToken cancellationToken)
    {
        try
        {
            // ---- customer (L2 step 4) ----
            var customerLookup = await Guarded(() => _gateway.FindCustomerAsync(claim.CustomerReference, cancellationToken));
            var customerExisted = customerLookup is not null;
            var customer = customerLookup;
            if (customer is null)
            {
                if (claim.CustomerCreateFirstSentAt is { } customerSent)
                {
                    // An earlier create had an unknown outcome and the reference is still not there.
                    if (Now - customerSent < UnknownOutcomeWindow)
                        return await StillPendingAsync(claim, "The customer record is still being created.");
                    if (claim.CustomerCreateConflicted)
                        claim.RenewCustomerToken(Now); // R11 step 4: 409 + 120 s + not found
                }

                claim.RecordCustomerCreateSent(Now);
                await _db.SaveChangesAsync(CancellationToken.None);

                CustomerCreateResult created;
                try
                {
                    created = await Guarded(() => _gateway.CreateCustomerAsync(claim.CustomerReference,
                        claim.CustomerEmail, claim.CustomerUniquenessToken, cancellationToken));
                }
                catch (SubscriptionBillingException rejected) when (rejected.Error == SubscriptionBillingError.Rejected)
                {
                    // The token is spent; a corrected attempt is a new claim with new tokens.
                    claim.Reject("customer create rejected", Now);
                    await _db.SaveChangesAsync(CancellationToken.None);
                    throw;
                }
                if (created.Customer is null)
                {
                    claim.RecordCustomerCreateUnknown(created.Conflicted, created.Reason ?? "unknown", Now);
                    return await StillPendingAsync(claim, "The customer record is still being created.");
                }

                customer = created.Customer;
                _logger.LogInformation("Maxio customer {CustomerId} ready for signup {SignupId}", customer.Id, claim.Id);
            }

            // ---- a plan the customer already holds under another reference (L2 step 6, L3, R16) ----
            if (customerExisted)
            {
                var held = await FindHeldAsync(claim, customer, cancellationToken);
                if (held?.Id is int heldId)
                {
                    claim.Complete(heldId, Now);
                    await _db.SaveChangesAsync(CancellationToken.None);
                    _logger.LogInformation("Signup {SignupId}: customer already holds subscription {SubscriptionId} ({State})",
                        claim.Id, heldId, held.State?.Value);
                    return Settled(SubscribeOutcome.AlreadySubscribed, claim, held);
                }
            }

            // ---- subscribe (L2 step 6) ----
            if (claim.SubscriptionCreateFirstSentAt is { } subscriptionSent)
            {
                // An earlier create of this signup may have been sent and its outcome is not settled (R11).
                var landed = await Guarded(() =>
                    _gateway.FindSubscriptionByReferenceAsync(claim.SubscriptionReference, cancellationToken));
                if (landed is not null)
                    return await SettleFoundAsync(claim, landed, SubscribeOutcome.Created);

                // Not there. Before 120 s it may still be running: do not send again yet.
                if (Now - subscriptionSent < UnknownOutcomeWindow)
                    return await StillPendingAsync(claim, "The subscription is still being created.");

                // R11 step 4: after a 409 and 120 s with nothing holding our unique reference, the first
                // copy created nothing, so start again with a new token. Otherwise resend with the same one.
                if (claim.SubscriptionCreateConflicted)
                    claim.RenewSubscriptionToken(Now);
            }

            claim.RecordSubscriptionCreateSent(Now);
            await _db.SaveChangesAsync(CancellationToken.None);

            SignupResult result;
            try
            {
                result = await _gateway.SignUpAsync(claim.CustomerReference, claim.PlanHandle,
                    claim.SubscriptionReference, claim.SubscriptionUniquenessToken, site.InvoiceCollectionMethod);
            }
            catch (ApiException<CreateSubscriptionError> rejected) when ((int)rejected.StatusCode == 422)
            {
                return await HandleCreateRejectionAsync(claim, rejected);
            }
            catch (ApiException<CreateSubscriptionError> rejected) when ((int)rejected.StatusCode is >= 400 and < 500)
            {
                // 401/403/404 and the like: a definite "no" that no resend fixes.
                claim.Reject(MaxioErrors.Describe(rejected), Now);
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogWarning("Maxio rejected signup {SignupId}: {Reason}", claim.Id, MaxioErrors.Describe(rejected));
                throw new SubscriptionBillingException(SubscriptionBillingError.Rejected,
                    "The billing system rejected the subscription.", innerException: rejected);
            }
            catch (Exception ex) when (ex is SdkException or InvalidOperationException)
            {
                // SafeSignup throws only when nothing was sent (its lookup failed) or for an answer that is
                // not a rejection; the claim stays open with its token and is resumed later.
                _logger.LogWarning(ex, "Signup {SignupId} could not be sent: {Reason}", claim.Id, MaxioErrors.Describe(ex));
                throw new SubscriptionBillingException(SubscriptionBillingError.ProviderUnavailable,
                    "The billing system is unavailable. Try again later.", TimeSpan.FromSeconds(30), ex);
            }

            return await result.Match<Task<SubscribeResult?>>(
                created => SettleFoundAsync(claim, created.Subscription, SubscribeOutcome.Created),
                existing => SettleFoundAsync(claim, existing.Subscription, SubscribeOutcome.Created),
                recovered => SettleFoundAsync(claim, recovered.Subscription, SubscribeOutcome.Created),
                async unknown =>
                {
                    claim.RecordSubscriptionCreateUnknown(unknown.FirstCopyArrived, MaxioErrors.Describe(unknown.CreateFailure), Now);
                    _logger.LogWarning(unknown.CreateFailure,
                        "Signup {SignupId} has an unknown outcome (first copy arrived: {Arrived}); it will be settled under reference {Reference}",
                        claim.Id, unknown.FirstCopyArrived, claim.SubscriptionReference);
                    return await StillPendingAsync(claim, "The subscription is still being created.");
                });
        }
        finally
        {
            if (_db.Entry(claim).State != EntityState.Detached)
            {
                claim.ReleaseLease(Now);
                try
                {
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogWarning(ex, "Could not release the lease of signup {SignupId}; it expires on its own", claim.Id);
                }
            }
        }
    }

    private async Task<SubscribeResult?> SettleFoundAsync(SubscriptionSignup claim, Subscription subscription,
        SubscribeOutcome outcomeWhenLive)
    {
        switch (subscription.State.Standing())
        {
            case SubscriptionStanding.Ended:
                // A subscription under this reference existed and has ended: this attempt is spent.
                claim.MarkEnded(Now);
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("Signup {SignupId}: subscription under its reference has ended ({State}); claiming a new attempt",
                    claim.Id, subscription.State?.Value);
                return null;

            default:
                // Live, transient, recoverable or a state the SDK does not know: the subscription exists.
                if (subscription.Id is not int id)
                    throw new SubscriptionBillingException(SubscriptionBillingError.ProviderUnavailable,
                        "The billing system returned a subscription without an id.");
                claim.Complete(id, Now);
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("Signup {SignupId} settled: subscription {SubscriptionId} is {State}",
                    claim.Id, id, subscription.State?.Value);
                return Settled(outcomeWhenLive, claim, subscription);
        }
    }

    private async Task<SubscribeResult> HandleCreateRejectionAsync(SubscriptionSignup claim,
        ApiException<CreateSubscriptionError> rejected)
    {
        if (rejected.Error.TryGetErrorListResponse1(out var errors) &&
            errors.AdditionalProperties.TryGetValue<string>("action_link", out var actionLink) &&
            !string.IsNullOrWhiteSpace(actionLink))
        {
            // 3-D Secure: not a decline and not a validation error. Do not resend; settle by reference later.
            claim.RecordActionRequired(actionLink, Now);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Signup {SignupId} needs customer action (3-D Secure)", claim.Id);
            return new SubscribeResult(SubscribeOutcome.ActionRequired, claim.Id, null, actionLink);
        }

        // A received request spends its token even when rejected: a later corrected attempt is a new claim.
        var reason = MaxioErrors.Describe(rejected);
        claim.Reject(reason, Now);
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.LogWarning("Maxio rejected signup {SignupId}: {Reason}", claim.Id, reason);
        throw new SubscriptionBillingException(SubscriptionBillingError.Rejected,
            "The billing system rejected the subscription.", innerException: rejected);
    }

    private async Task<SubscribeResult> StillPendingAsync(SubscriptionSignup claim, string why)
    {
        if (claim.Status == SubscriptionSignupStatus.Pending)
            claim.RecordSubscriptionCreateUnknown(claim.SubscriptionCreateConflicted, why, Now);
        await _db.SaveChangesAsync(CancellationToken.None);
        return new SubscribeResult(SubscribeOutcome.Pending, claim.Id, null, RetryAfter: UnknownOutcomeWindow);
    }

    private Task<Subscription?> FindHeldAsync(SubscriptionSignup claim, Customer customer, CancellationToken cancellationToken) =>
        Guarded(() => _gateway.FindHeldSubscriptionAsync(customer, claim.PlanHandle, claim.SubscriptionReference, cancellationToken));

    private async Task<bool> TryLeaseAsync(SubscriptionSignup claim, CancellationToken cancellationToken)
    {
        if (claim.IsLeased(Now)) return false;
        claim.AcquireLease(Now);
        try
        {
            // Only one worker's update matches the concurrency token it read.
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _db.Entry(claim).ReloadAsync(cancellationToken);
            return false;
        }
    }

    // ================================================================= helpers

    private DateTimeOffset Now => _time.GetUtcNow();

    private static SubscriptionBillingException InProgress() =>
        new(SubscriptionBillingError.SignupInProgress,
            "A subscription request for this plan is already in progress. Check your subscriptions shortly.",
            TimeSpan.FromSeconds(10));

    private static SubscribeResult Settled(SubscribeOutcome outcome, SubscriptionSignup claim, Subscription subscription) =>
        new(outcome, claim.Id, ToCustomerSubscription(subscription, subscription.Currency));

    /// <summary>
    /// Runs a Maxio call and maps a failure that is not already a <see cref="SubscriptionBillingException"/>
    /// to <see cref="SubscriptionBillingError.ProviderUnavailable"/>, without passing Maxio's text on.
    /// </summary>
    private async Task<T> Guarded<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (SubscriptionBillingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SdkException or PageCeilingException or InvalidOperationException or TimeoutException)
        {
            _logger.LogWarning(ex, "Maxio call failed: {Reason}", MaxioErrors.Describe(ex));
            throw new SubscriptionBillingException(SubscriptionBillingError.ProviderUnavailable,
                "The billing system is unavailable. Try again later.", TimeSpan.FromSeconds(30), ex);
        }
    }

    private static bool IsDuplicateKey(Exception ex) => ex switch
    {
        // SQL Server: primary-key (2627) or unique-index (2601) violation.
        DbUpdateException { InnerException: SqlException { Number: 2627 or 2601 } } => true,
        // In-memory provider: the store refuses a second row with the same key.
        ArgumentException argument => argument.Message.Contains("same key", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    internal static SubscriptionPlan ToPlan(Product product, string? currency)
    {
        var cents = product.PriceInCents ?? 0;
        return new SubscriptionPlan(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            cents / 100m,
            cents,
            currency,
            product.Interval,
            product.IntervalUnit?.Value,
            product.RequireCreditCard);
    }

    internal static CustomerSubscription ToCustomerSubscription(Subscription subscription, string? currency) =>
        new(
            subscription.Id ?? 0,
            subscription.Product?.Handle,
            subscription.Product?.Name,
            subscription.Product?.ProductFamily?.Handle,
            subscription.ProductPriceInCents is long cents ? cents / 100m : null,
            subscription.ProductPriceInCents,
            subscription.Currency ?? currency,
            subscription.State?.Value,
            subscription.State.HasAccess(),
            subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodStartedAt,
            subscription.CreatedAt,
            subscription.PaymentCollectionMethod?.Value,
            subscription.Reference);
}
