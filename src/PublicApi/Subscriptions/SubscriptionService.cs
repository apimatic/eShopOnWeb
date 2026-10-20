using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Subscribe flow: validate plan -> claim the user's enrollment row -> ensure customer -> create subscription -> record.
/// The claim is written before any Maxio write, so a double-click cannot create two customers or subscriptions.
/// </summary>
public class SubscriptionService
{
    /// <summary>Whole-call budget shared by every Maxio call of one request.</summary>
    public static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    /// <summary>Budget for the re-read that settles an unknown write (independent of the caller's cancellation).</summary>
    public static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);
    /// <summary>A Claimed row older than this was abandoned (crash / timeout) and is settled like an Unknown one.</summary>
    public static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);

    public const string CustomerReferencePrefix = "eshop-customer-";
    public const string SubscriptionReferencePrefix = "eshop-subscription-";
    private const string PlaceholderLastName = "Customer";

    private readonly MaxioBillingGateway _gateway;
    private readonly ISubscriptionEnrollmentStore _store;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionService(MaxioBillingGateway gateway, ISubscriptionEnrollmentStore store, ILogger<SubscriptionService> logger, TimeProvider? time = null)
    {
        _gateway = gateway;
        _store = store;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    // ---------------------------------------------------------------- plans

    public Task<PlanListing> ListPlansAsync(CancellationToken ct)
        => Bounded(token => ListPlansCoreAsync(token), ct);

    private async Task<PlanListing> ListPlansCoreAsync(CancellationToken token)
    {
        try
        {
            return await _gateway.ListPlansAsync(token);
        }
        catch (MaxioException ex)
        {
            throw ToFlow(ex);
        }
    }

    // ------------------------------------------------------------ subscribe

    public Task<SubscriptionView> SubscribeAsync(SubscribeCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.PlanHandle))
            throw new SubscriptionFlowException(400, "plan_required", "planHandle is required.");
        if (command.PlanHandle.Length > 256)
            throw new SubscriptionFlowException(400, "unknown_plan", "The requested plan is not offered.");
        if (string.IsNullOrWhiteSpace(command.Email))
            throw new SubscriptionFlowException(422, "email_required", "The account has no email address to bill.");

        return Bounded(token => SubscribeCoreAsync(command, command.PlanHandle.Trim(), token), ct);
    }

    private async Task<SubscriptionView> SubscribeCoreAsync(SubscribeCommand cmd, string planHandle, CancellationToken token)
    {
        // 1. the plan must be one the family offers right now (re-listed, never hard-coded), before any write
        PlanListing listing;
        try
        {
            listing = await _gateway.ListPlansAsync(token);
        }
        catch (MaxioException ex)
        {
            throw ToFlow(ex);
        }

        var plan = listing.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (!listing.IsComplete)
            {
                _logger.LogWarning("Plan {Plan} not found in an incomplete plan listing; refusing as unavailable.", planHandle);
                throw new SubscriptionFlowException(503, "plan_set_unavailable", "The plan list is temporarily unavailable. Try again later.");
            }
            _logger.LogWarning("Plan {Plan} rejected: not offered by the product family.", planHandle);
            throw new SubscriptionFlowException(400, "unknown_plan", "The requested plan is not offered.");
        }

        // 2. claim (atomic refusal by the store) - at most one retry after a stale/unknown row was settled away
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var candidate = new SubscriptionEnrollment(
                cmd.UserId, plan.Handle, CustomerReferencePrefix + cmd.UserId, SubscriptionReferencePrefix + cmd.UserId, _time.GetUtcNow());
            var claim = await _store.TryClaimAsync(candidate, CancellationToken.None);

            if (claim.Claimed)
            {
                return await RunClaimedAsync(claim.Enrollment, plan, cmd, token);
            }

            var row = claim.Enrollment;
            _logger.LogWarning("Claim refused for user {UserId}: existing enrollment is {Status} for plan {Plan}; no Maxio call made.",
                cmd.UserId, row.Status, row.PlanHandle);

            if (row.Status == EnrollmentStatus.Claimed && !IsStale(row))
            {
                throw new SubscriptionFlowException(409, "in_progress", "A subscription request for this account is already being processed.");
            }

            if (row.Status == EnrollmentStatus.Active)
            {
                if (!string.Equals(row.PlanHandle, plan.Handle, StringComparison.Ordinal))
                    throw new SubscriptionFlowException(409, "already_subscribed", "This account already has a subscription.");
                return await ViewForAsync(row, token);
            }

            // Unknown (or an abandoned claim): settle by re-reading Maxio, never by writing again blindly
            var settled = await SettleAsync(row);
            if (settled is not null)
            {
                if (!string.Equals(row.PlanHandle, plan.Handle, StringComparison.Ordinal))
                    throw new SubscriptionFlowException(409, "already_subscribed", "This account already has a subscription.");
                return ToView(settled, row);
            }
            // not found at Maxio: SettleAsync released the row, so claim again
        }

        throw new SubscriptionFlowException(409, "in_progress", "A subscription request for this account is already being processed.");
    }

    private async Task<SubscriptionView> RunClaimedAsync(SubscriptionEnrollment row, SubscriptionPlan plan, SubscribeCommand cmd, CancellationToken token)
    {
        var subscriptionWriteStarted = false;
        try
        {
            // 3. ensure the Maxio customer (found by deterministic reference, created only if absent)
            var customerId = await EnsureCustomerAsync(row, cmd, token);
            row.RecordCustomer(customerId, _time.GetUtcNow());
            await _store.SaveAsync(row, CancellationToken.None);

            // 4. create the subscription
            subscriptionWriteStarted = true;
            MaxioSubscriptionInfo info;
            try
            {
                info = await _gateway.CreateSubscriptionAsync(plan.Handle, customerId, row.SubscriptionReference, token);
            }
            catch (Exception ex) when (IsUnknownOutcome(ex))
            {
                // the write may have landed: settle it here, in its own catch
                _logger.LogWarning("CreateSubscription outcome unknown for user {UserId}; re-reading by reference.", cmd.UserId);
                var found = await SettleAsync(row);
                if (found is null)
                    throw new SubscriptionFlowException(503, "subscription_not_created", "The subscription could not be created. Try again.", ex);
                return ToView(found, row);
            }
            catch (MaxioException ex)
            {
                // definitive refusal (4xx / credentials): nothing was created, release the claim
                await _store.ReleaseAsync(row, CancellationToken.None);
                throw ToFlow(ex);
            }

            // 5. record
            row.MarkActive(info.Id, info.CustomerId ?? customerId, info.State, _time.GetUtcNow());
            await _store.SaveAsync(row, CancellationToken.None);
            _logger.LogInformation("Subscribed user {UserId} to {Plan}: subscription {SubscriptionId} state {State}.",
                cmd.UserId, plan.Handle, info.Id, info.State);
            return ToView(info, row);
        }
        catch (Exception ex) when (!subscriptionWriteStarted && ex is not SubscriptionFlowException)
        {
            // failure before the subscription write: nothing exists that the reference lookup cannot recover. Free the claim.
            if (row.Status == EnrollmentStatus.Claimed)
            {
                await _store.ReleaseAsync(row, CancellationToken.None);
            }
            if (ex is MaxioException mex) throw ToFlow(mex);
            throw;
        }
        catch (SubscriptionFlowException) when (!subscriptionWriteStarted && row.Status == EnrollmentStatus.Claimed)
        {
            await _store.ReleaseAsync(row, CancellationToken.None);
            throw;
        }
    }

    private async Task<int> EnsureCustomerAsync(SubscriptionEnrollment row, SubscribeCommand cmd, CancellationToken token)
    {
        if (row.MaxioCustomerId is int known) return known;

        try
        {
            if (await _gateway.FindCustomerIdAsync(row.CustomerReference, token) is int existing)
                return existing;

            var (first, last) = SplitName(cmd);
            return await _gateway.CreateCustomerAsync(row.CustomerReference, first, last, cmd.Email!, token);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            _logger.LogWarning("CreateCustomer outcome unknown for user {UserId}; re-reading by reference.", cmd.UserId);
            var (outcome, id) = await ReconcileAsync(t => _gateway.FindCustomerIdAsync(row.CustomerReference, t));
            switch (outcome)
            {
                case Reconciled.Found:
                    return id!.Value;
                case Reconciled.NotFound:
                    // the customer write did not land; free the claim (done by the caller's catch)
                    throw new SubscriptionFlowException(503, "customer_not_created", "The billing profile could not be created. Try again.", ex);
                default:
                    row.MarkUnknown(_time.GetUtcNow());
                    await _store.SaveAsync(row, CancellationToken.None);
                    throw new SubscriptionFlowException(504, "outcome_unknown", "The result is not yet known. Check your subscriptions shortly.", ex);
            }
        }
    }

    // --------------------------------------------------------- my subscriptions

    public Task<IReadOnlyList<SubscriptionView>> GetMySubscriptionsAsync(string userId, CancellationToken ct)
        => Bounded<IReadOnlyList<SubscriptionView>>(async token =>
        {
            var row = await _store.GetAsync(userId, token);
            if (row is null) return Array.Empty<SubscriptionView>();

            if (row.Status == EnrollmentStatus.Unknown || (row.Status == EnrollmentStatus.Claimed && IsStale(row)))
            {
                try
                {
                    var settled = await SettleAsync(row);
                    if (settled is null) return Array.Empty<SubscriptionView>(); // never landed; row released
                }
                catch (SubscriptionFlowException)
                {
                    return new[] { PendingView(row, "unknown") };
                }
            }
            else if (row.Status == EnrollmentStatus.Claimed)
            {
                return new[] { PendingView(row, "processing") };
            }

            return new[] { await ViewForAsync(row, token) };
        }, ct);

    // ---------------------------------------------------------------- helpers

    /// <summary>Live read for an Active row; falls back to the stored copy (IsLive=false) if Maxio cannot be read.</summary>
    private async Task<SubscriptionView> ViewForAsync(SubscriptionEnrollment row, CancellationToken token)
    {
        if (row.MaxioSubscriptionId is int id)
        {
            try
            {
                var info = await _gateway.ReadSubscriptionAsync(id, token);
                if (info is not null) return ToView(info, row);
                _logger.LogWarning("Subscription {SubscriptionId} for user {UserId} is not found at Maxio.", id, row.UserId);
            }
            catch (MaxioException ex)
            {
                _logger.LogWarning("Live read of subscription {SubscriptionId} failed ({Kind}); returning stored copy.", id, ex.Kind);
            }
        }
        return new SubscriptionView
        {
            PlanHandle = row.PlanHandle,
            State = row.SubscriptionState ?? "unknown",
            SubscriptionId = row.MaxioSubscriptionId,
            IsLive = false
        };
    }

    private static SubscriptionView PendingView(SubscriptionEnrollment row, string state)
        => new() { PlanHandle = row.PlanHandle, State = state, IsLive = false };

    private static SubscriptionView ToView(MaxioSubscriptionInfo info, SubscriptionEnrollment row) => new()
    {
        PlanHandle = info.PlanHandle ?? row.PlanHandle,
        PlanName = info.PlanName,
        PriceInCents = info.PriceInCents,
        Interval = info.Interval,
        IntervalUnit = info.IntervalUnit,
        State = info.State ?? "unknown",
        NextBillingDate = info.NextAssessmentAt,
        CurrentPeriodEndsAt = info.CurrentPeriodEndsAt,
        SubscriptionId = info.Id,
        IsLive = true
    };

    private bool IsStale(SubscriptionEnrollment row) => _time.GetUtcNow() - row.UpdatedUtc > StaleClaimAge;

    private static bool IsUnknownOutcome(Exception ex)
        => ex is OperationCanceledException || ex is MaxioException { Kind: MaxioFailureKind.OutcomeUnknown };

    /// <summary>
    /// Settles a row whose subscription write may have landed, by FindSubscription on its reference.
    /// Found: row becomes Active, info returned. Not found: row released, null returned.
    /// Re-read failed: row stays Unknown and a 504 flow error is thrown (the my-subscriptions sweep retries it).
    /// </summary>
    private async Task<MaxioSubscriptionInfo?> SettleAsync(SubscriptionEnrollment row)
    {
        var (outcome, info) = await ReconcileAsync(t => _gateway.FindSubscriptionAsync(row.SubscriptionReference, t));
        switch (outcome)
        {
            case Reconciled.Found:
                row.MarkActive(info!.Id, info.CustomerId, info.State, _time.GetUtcNow());
                await _store.SaveAsync(row, CancellationToken.None);
                _logger.LogInformation("Settled enrollment for user {UserId}: subscription {SubscriptionId} exists.", row.UserId, info.Id);
                return info;
            case Reconciled.NotFound:
                await _store.ReleaseAsync(row, CancellationToken.None);
                _logger.LogWarning("Settled enrollment for user {UserId}: no subscription at Maxio; claim released.", row.UserId);
                return null;
            default:
                row.MarkUnknown(_time.GetUtcNow());
                await _store.SaveAsync(row, CancellationToken.None);
                _logger.LogWarning("Could not settle enrollment for user {UserId}; left Unknown.", row.UserId);
                throw new SubscriptionFlowException(504, "outcome_unknown", "The result is not yet known. Check your subscriptions shortly.");
        }
    }

    private enum Reconciled { Found, NotFound, Failed }

    private async Task<(Reconciled, T?)> ReconcileAsync<T>(Func<CancellationToken, Task<T?>> reread) where T : struct
    {
        using var cts = new CancellationTokenSource(SettleBudget);
        try
        {
            var value = await reread(cts.Token);
            return value is null ? (Reconciled.NotFound, null) : (Reconciled.Found, value);
        }
        catch (Exception ex) when (ex is MaxioException || ex is OperationCanceledException)
        {
            return (Reconciled.Failed, null);
        }
    }

    private async Task<(Reconciled, MaxioSubscriptionInfo?)> ReconcileAsync(Func<CancellationToken, Task<MaxioSubscriptionInfo?>> reread)
    {
        using var cts = new CancellationTokenSource(SettleBudget);
        try
        {
            var value = await reread(cts.Token);
            return value is null ? (Reconciled.NotFound, null) : (Reconciled.Found, value);
        }
        catch (Exception ex) when (ex is MaxioException || ex is OperationCanceledException)
        {
            return (Reconciled.Failed, null);
        }
    }

    private static (string First, string Last) SplitName(SubscribeCommand cmd)
    {
        var source = !string.IsNullOrWhiteSpace(cmd.UserName) ? cmd.UserName! : cmd.Email!;
        var at = source.IndexOf('@');
        var first = at > 0 ? source[..at] : source;
        return (string.IsNullOrWhiteSpace(first) ? "Shopper" : first, PlaceholderLastName);
    }

    private static SubscriptionFlowException ToFlow(MaxioException ex) => ex.Kind switch
    {
        MaxioFailureKind.Rejected when ex.StatusCode == 422 =>
            new SubscriptionFlowException(422, "provider_rejected", "The billing provider rejected the request details.", ex),
        MaxioFailureKind.Rejected =>
            new SubscriptionFlowException(502, "provider_error", "The billing provider could not process the request.", ex),
        MaxioFailureKind.OutcomeUnknown =>
            new SubscriptionFlowException(504, "outcome_unknown", "The result is not yet known. Check your subscriptions shortly.", ex),
        _ => new SubscriptionFlowException(503, "provider_unavailable", "The billing provider is temporarily unavailable.", ex)
    };

    /// <summary>One whole-call budget for every Maxio call of a request, linked to the request's own cancellation.</summary>
    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken callerToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            throw new SubscriptionFlowException(504, "timeout", "The billing provider did not answer in time.", ex);
        }
    }
}
