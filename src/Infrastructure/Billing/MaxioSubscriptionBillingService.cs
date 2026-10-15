using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// Subscribe order: validate the plan against the family's catalog → claim (primary-key insert of
/// <see cref="SubscriptionEnrollment"/>) → ensure customer → pre-check Maxio for a live subscription → create → record.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioPlanCatalog _catalog;
    private readonly MaxioCustomerProvisioner _customers;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioAdvancedBillingClient client, MaxioPlanCatalog catalog,
        MaxioCustomerProvisioner customers, MaxioSettings settings, CatalogContext db, TimeProvider clock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _catalog = catalog;
        _customers = customers;
        _settings = settings;
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = MaxioBillingLimits.StartBudget(MaxioBillingLimits.ReadBudget, cancellationToken);
        try
        {
            return await _catalog.ReadAsync(budget.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw MaxioFailures.BudgetExceeded(ex);
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        planHandle = planHandle.Trim();

        using var budget = MaxioBillingLimits.StartBudget(MaxioBillingLimits.WriteBudget, cancellationToken);
        try
        {
            // The handle must be one this store offers: a plan in the configured family's catalog.
            var catalog = await _catalog.ReadAsync(budget.Token);
            if (!catalog.Plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
            {
                if (catalog.Truncated)
                {
                    throw new BillingProviderException(BillingFailureKind.Misconfigured,
                        "The subscription plan catalog is too large to validate this plan.");
                }
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            // A completed enrollment whose subscription has since ended is released and claimed again once.
            for (var attempt = 0; ; attempt++)
            {
                var (enrollment, owned) = await ClaimAsync(userName, planHandle, budget.Token);
                if (!owned)
                {
                    var current = await ReadCompletedAsync(enrollment, budget.Token);
                    if (current is not null && IsLive(current))
                    {
                        return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, planHandle, Map(current));
                    }
                    if (attempt > 0)
                    {
                        throw new SubscriptionInProgressException("Your subscription is being updated. Please try again.");
                    }
                    await ReleaseCompletedAsync(enrollment);
                    continue;
                }

                return await SubscribeOwnedAsync(enrollment, userName, planHandle, budget.Token);
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw MaxioFailures.BudgetExceeded(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        using var budget = MaxioBillingLimits.StartBudget(MaxioBillingLimits.ReadBudget, cancellationToken);
        try
        {
            var customerId = await _customers.FindCustomerIdAsync(userName, budget.Token);
            if (customerId is null)
            {
                return Array.Empty<SubscriptionDetails>();
            }

            await SettleUnknownEnrollmentsAsync(userName);

            var familyHandle = _settings.ProductFamilyHandle!.Trim();
            var subscriptions = await ListCustomerSubscriptionsAsync(customerId.Value, budget.Token);
            return subscriptions
                .Where(s => s.Id is not null)
                .Where(s => s.Product?.ProductFamily?.Handle is null
                            || string.Equals(s.Product.ProductFamily.Handle, familyHandle, StringComparison.Ordinal))
                .OrderByDescending(s => s.CreatedAt)
                .Select(Map)
                .ToList();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw MaxioFailures.BudgetExceeded(ex);
        }
    }

    private async Task<SubscribeResult> SubscribeOwnedAsync(SubscriptionEnrollment enrollment, string userName, string planHandle,
        CancellationToken cancellationToken)
    {
        // A claim taken over from an unknown outcome is settled first: the earlier create may have landed.
        if (enrollment.ProviderCustomerId is int knownCustomerId)
        {
            var settled = await TrySettleTakenOverAsync(enrollment, knownCustomerId, planHandle);
            if (settled is not null)
            {
                return settled;
            }
        }

        int customerId;
        try
        {
            customerId = await _customers.EnsureCustomerAsync(userName, cancellationToken);
            enrollment.AttachCustomer(customerId, _clock.GetUtcNow());
            await _db.SaveChangesAsync(CancellationToken.None);

            // Restart safety: the local claim store may have been wiped (in-memory database) while Maxio still
            // holds a live subscription to this plan; adopt it instead of creating a second one.
            var existing = (await ListCustomerSubscriptionsAsync(customerId, cancellationToken))
                .FirstOrDefault(s => s.Id is not null && IsLive(s)
                                     && string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal));
            if (existing is not null)
            {
                enrollment.Complete(existing.Id!.Value, existing.Reference, _clock.GetUtcNow());
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("Adopted existing Maxio subscription {SubscriptionId} for plan {PlanHandle}.", existing.Id, planHandle);
                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, planHandle, Map(existing));
            }
        }
        catch
        {
            // Nothing was written to Maxio's subscriptions yet: free the claim so the user can retry.
            await ReleaseAsync(enrollment);
            throw;
        }

        return await CreateSubscriptionAsync(enrollment, customerId, planHandle, cancellationToken);
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(SubscriptionEnrollment enrollment, int customerId, string planHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = enrollment.SubscriptionReference,
                            // No card is captured by this store, so the subscription is collected by invoice.
                            PaymentCollectionMethod = _settings.CollectionMethod
                        }
                    }
                },
                cancellationToken: cancellationToken);

            var subscription = response.Subscription;
            if (subscription?.Id is int subscriptionId)
            {
                enrollment.Complete(subscriptionId, subscription.Reference, _clock.GetUtcNow());
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {PlanHandle}.",
                    subscriptionId, subscription.State?.Value, customerId, planHandle);
                return new SubscribeResult(SubscribeOutcome.Created, planHandle, Map(subscription));
            }
            _logger.LogWarning("Maxio accepted subscription {SubscriptionReference} but returned no id; reading it back.",
                enrollment.SubscriptionReference);
            return await SettleUnknownCreateAsync(enrollment, customerId, planHandle, cause: null);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var body))
            {
                _logger.LogWarning("Maxio refused subscription for customer {CustomerId} on plan {PlanHandle} (HTTP {Status}): {Reasons}",
                    customerId, planHandle, (int)ex.StatusCode, string.Join("; ", body.Errors));
                await ReleaseAsync(enrollment);
                throw new BillingProviderException(BillingFailureKind.Rejected,
                    "The billing provider could not create the subscription.", (int)ex.StatusCode, body.Errors, ex);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                if (MaxioFailures.IsAmbiguousWriteStatus(raw.StatusCode))
                {
                    return await SettleUnknownCreateAsync(enrollment, customerId, planHandle, ex);
                }
                _logger.LogWarning("Maxio refused subscription for customer {CustomerId} on plan {PlanHandle} with HTTP {Status}.",
                    customerId, planHandle, (int)raw.StatusCode);
                await ReleaseAsync(enrollment);
                throw MaxioFailures.FromStatus(raw.StatusCode, ex, callerSuppliedInput: true);
            }
            await ReleaseAsync(enrollment);
            throw MaxioFailures.FromStatus(ex.StatusCode, ex, callerSuppliedInput: true);
        }
        catch (ResponseDeserializationException ex) when (MaxioFailures.IsAmbiguousWriteStatus(ex.StatusCode))
        {
            return await SettleUnknownCreateAsync(enrollment, customerId, planHandle, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogWarning(ex, "Maxio refused subscription for customer {CustomerId} with an unreadable HTTP {Status} body.",
                customerId, (int)ex.StatusCode);
            await ReleaseAsync(enrollment);
            throw MaxioFailures.FromStatus(ex.StatusCode, ex, callerSuppliedInput: true);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleUnknownCreateAsync(enrollment, customerId, planHandle, ex);
        }
        catch (OperationCanceledException ex)
        {
            return await SettleUnknownCreateAsync(enrollment, customerId, planHandle, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials could not be applied.");
            await ReleaseAsync(enrollment);
            throw MaxioFailures.FromSdk(ex);
        }
    }

    /// <summary>
    /// The create may have landed. Re-read by the reference stored on the claim: found → record it as created;
    /// definitively absent → release the claim and report a retryable failure; re-read failed → keep the claim
    /// Unknown and report the subscription as pending confirmation (the next request settles it first).
    /// </summary>
    private async Task<SubscribeResult> SettleUnknownCreateAsync(SubscriptionEnrollment enrollment, int customerId, string planHandle,
        Exception? cause)
    {
        _logger.LogWarning(cause, "Outcome of creating Maxio subscription {SubscriptionReference} is unknown; reading it back by reference.",
            enrollment.SubscriptionReference);

        Subscription? found;
        try
        {
            found = await FindByReferenceWithFreshBudgetAsync(enrollment.SubscriptionReference, customerId, planHandle);
        }
        catch (Exception readFailure) when (readFailure is BillingProviderException or OperationCanceledException)
        {
            _logger.LogError(readFailure, "Could not settle the outcome of creating Maxio subscription {SubscriptionReference}.",
                enrollment.SubscriptionReference);
            enrollment.MarkUnknown(_clock.GetUtcNow());
            await _db.SaveChangesAsync(CancellationToken.None);
            return new SubscribeResult(SubscribeOutcome.PendingConfirmation, planHandle, null);
        }

        if (found is not null)
        {
            enrollment.Complete(found.Id!.Value, found.Reference, _clock.GetUtcNow());
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Settled: Maxio subscription {SubscriptionId} was created for reference {SubscriptionReference}.",
                found.Id, enrollment.SubscriptionReference);
            return new SubscribeResult(SubscribeOutcome.Created, planHandle, Map(found));
        }

        _logger.LogWarning("Settled: Maxio subscription {SubscriptionReference} was not created.", enrollment.SubscriptionReference);
        await ReleaseAsync(enrollment);
        throw new BillingProviderException(BillingFailureKind.Unavailable,
            "The billing provider did not create the subscription. Please try again.", innerException: cause);
    }

    /// <summary>
    /// For a claim taken over from an earlier request whose outcome was unknown: returns the result when the earlier
    /// create is found (or still cannot be confirmed), or null when it definitively did not happen and creation should proceed.
    /// </summary>
    private async Task<SubscribeResult?> TrySettleTakenOverAsync(SubscriptionEnrollment enrollment, int customerId, string planHandle)
    {
        Subscription? found;
        try
        {
            found = await FindByReferenceWithFreshBudgetAsync(enrollment.SubscriptionReference, customerId, planHandle);
        }
        catch (Exception readFailure) when (readFailure is BillingProviderException or OperationCanceledException)
        {
            _logger.LogError(readFailure, "Still cannot settle Maxio subscription {SubscriptionReference}.", enrollment.SubscriptionReference);
            enrollment.MarkUnknown(_clock.GetUtcNow());
            await _db.SaveChangesAsync(CancellationToken.None);
            return new SubscribeResult(SubscribeOutcome.PendingConfirmation, planHandle, null);
        }

        if (found is null)
        {
            return null;
        }
        enrollment.Complete(found.Id!.Value, found.Reference, _clock.GetUtcNow());
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Settled: earlier Maxio subscription {SubscriptionId} was created for reference {SubscriptionReference}.",
            found.Id, enrollment.SubscriptionReference);
        return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, planHandle, Map(found));
    }

    /// <summary>Best effort: settles this user's Unknown enrollments so "my subscriptions" reflects them. Failures are logged only.</summary>
    private async Task SettleUnknownEnrollmentsAsync(string userName)
    {
        var key = MaxioCustomerProvisioner.NormalizeUserName(userName);
        var unknown = await _db.SubscriptionEnrollments
            .Where(e => e.UserName == key && e.Status == BillingClaimStatus.Unknown && e.ProviderCustomerId != null)
            .ToListAsync();
        foreach (var enrollment in unknown)
        {
            try
            {
                var found = await FindByReferenceWithFreshBudgetAsync(enrollment.SubscriptionReference,
                    enrollment.ProviderCustomerId!.Value, enrollment.PlanHandle);
                if (found is not null)
                {
                    enrollment.Complete(found.Id!.Value, found.Reference, _clock.GetUtcNow());
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
                else
                {
                    await ReleaseAsync(enrollment);
                }
            }
            catch (Exception ex) when (ex is BillingProviderException or OperationCanceledException or DbUpdateException)
            {
                _logger.LogWarning(ex, "Could not settle Maxio subscription {SubscriptionReference} while listing.", enrollment.SubscriptionReference);
            }
        }
    }

    /// <summary>
    /// Finds the subscription created with <paramref name="reference"/> for this customer and plan; null when Maxio
    /// definitively has none. Falls back to the customer's subscription list when the reference lookup misses.
    /// </summary>
    private async Task<Subscription?> FindByReferenceWithFreshBudgetAsync(string reference, int customerId, string planHandle)
    {
        using var cts = new CancellationTokenSource(MaxioBillingLimits.ReconcileBudget);
        Subscription? candidate = null;
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: cts.Token);
            candidate = response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            if (!ex.Error.TryGetNoContent(out RawError _))
            {
                var status = ex.Error.TryGetRawError(out RawError raw) ? raw.StatusCode : ex.StatusCode;
                _logger.LogError("Finding Maxio subscription {SubscriptionReference} failed with HTTP {Status}.", reference, (int)status);
                throw MaxioFailures.FromStatus(status, ex);
            }
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Finding Maxio subscription {SubscriptionReference} failed.", reference);
            throw MaxioFailures.FromSdk(ex);
        }

        if (Matches(candidate, reference, customerId, planHandle))
        {
            return candidate;
        }

        var all = await ListCustomerSubscriptionsAsync(customerId, cts.Token);
        return all.FirstOrDefault(s => Matches(s, reference, customerId, planHandle));
    }

    private static bool Matches(Subscription? subscription, string reference, int customerId, string planHandle) =>
        subscription?.Id is not null
        && string.Equals(subscription.Reference, reference, StringComparison.Ordinal)
        && (subscription.Customer?.Id is null || subscription.Customer.Id == customerId)
        && (subscription.Product?.Handle is null || string.Equals(subscription.Product.Handle, planHandle, StringComparison.Ordinal));

    private async Task<Subscription?> ReadCompletedAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        if (enrollment.ProviderCustomerId is not int customerId || enrollment.ProviderSubscriptionId is not int subscriptionId)
        {
            return null;
        }
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: cancellationToken);
            return response
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList();
        }
        catch (ApiException<RawError> ex)
        {
            _logger.LogError("Listing subscriptions of Maxio customer {CustomerId} failed with HTTP {Status}.", customerId, (int)ex.StatusCode);
            throw MaxioFailures.FromStatus(ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            _logger.LogError(ex, "Listing subscriptions of Maxio customer {CustomerId} failed.", customerId);
            throw MaxioFailures.FromSdk(ex);
        }
    }

    private async Task<(SubscriptionEnrollment Enrollment, bool Owned)> ClaimAsync(string userName, string planHandle,
        CancellationToken cancellationToken)
    {
        var key = MaxioCustomerProvisioner.NormalizeUserName(userName);
        var now = _clock.GetUtcNow();
        var claim = new SubscriptionEnrollment(key, planHandle, "eshop-sub-" + Guid.NewGuid().ToString("N"), now);
        _db.SubscriptionEnrollments.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return (claim, true);
        }
        catch (Exception ex) when (ClaimStore.IsDuplicateKey(ex))
        {
            // The composite primary key refused a second enrollment of this user in this plan.
            _db.Entry(claim).State = EntityState.Detached;
        }

        var existing = await _db.SubscriptionEnrollments
            .SingleOrDefaultAsync(e => e.UserName == key && e.PlanHandle == planHandle, cancellationToken);
        if (existing is null)
        {
            throw new SubscriptionInProgressException("Your subscription request is being processed. Please try again.");
        }
        if (existing is { Status: BillingClaimStatus.Completed, ProviderSubscriptionId: not null })
        {
            return (existing, false);
        }
        if (existing.Status == BillingClaimStatus.Pending && now - existing.UpdatedAt < MaxioBillingLimits.StaleClaimAfter)
        {
            _logger.LogInformation("Refused a concurrent subscribe for plan {PlanHandle}: a request is already in flight.", planHandle);
            throw new SubscriptionInProgressException("Your subscription request is already being processed.");
        }

        // Unknown or stale Pending: exactly one caller wins the takeover; it settles the earlier attempt first.
        existing.TakeOver(now);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(existing).State = EntityState.Detached;
            throw new SubscriptionInProgressException("Your subscription request is already being processed.");
        }
        return (existing, true);
    }

    private async Task ReleaseAsync(SubscriptionEnrollment enrollment)
    {
        try
        {
            _db.SubscriptionEnrollments.Remove(enrollment);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex)
        {
            _db.Entry(enrollment).State = EntityState.Detached;
            _logger.LogWarning(ex, "Could not release subscription claim {SubscriptionReference}.", enrollment.SubscriptionReference);
        }
    }

    /// <summary>Releases a completed enrollment whose subscription ended, so the plan can be subscribed to again.</summary>
    private async Task ReleaseCompletedAsync(SubscriptionEnrollment enrollment)
    {
        _db.SubscriptionEnrollments.Remove(enrollment);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            // Another request already released or replaced it (concurrency token); the next claim attempt decides.
            _db.Entry(enrollment).State = EntityState.Detached;
        }
    }

    private static bool IsLive(Subscription subscription) =>
        subscription.State is null
        || !(subscription.State == SubscriptionState.Canceled
             || subscription.State == SubscriptionState.Expired
             || subscription.State == SubscriptionState.FailedToCreate);

    private static SubscriptionDetails Map(Subscription s) => new(
        s.Id!.Value,
        s.Reference,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Currency,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.State?.Value ?? "unknown",
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.CurrentPeriodEndsAt,
        s.ActivatedAt,
        s.CreatedAt);
}
