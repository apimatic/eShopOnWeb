using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// The one class that fronts the Maxio Advanced Billing SDK: it owns the per-request time budget,
/// the duplicate claim, reconciliation of unknown write outcomes and the translation of every SDK
/// failure into a caller-safe <see cref="SubscriptionBillingException"/>.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>Products requested per page (the provider's maximum).</summary>
    public const int PlanPageSize = 200;

    /// <summary>Hard cap on product pages read; a listing that hits it is reported as incomplete.</summary>
    public const int MaxPlanPages = 5;

    /// <summary>Total budget for all Maxio calls made for one inbound request.</summary>
    public static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(30);

    /// <summary>Budget for re-reading the provider to settle a write whose outcome is unknown.</summary>
    public static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);

    /// <summary>How long a claim may stay unconfirmed before it is treated as abandoned.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    private const string SubscriptionReferencePrefix = "eshop-sub:";

    /// <summary>How every subscription created here collects payment: invoice/remittance, no payment method required.</summary>
    public static readonly CollectionMethod SubscriptionCollectionMethod = CollectionMethod.Remittance;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly TimeProvider _timeProvider;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger,
        TimeProvider timeProvider)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    // ------------------------------------------------------------------ plans

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        return await ReadPlansAsync(budget.Token, cancellationToken);
    }

    /// <summary>
    /// Walks the configured product family page by page. Stops when a page comes back short, or at
    /// <see cref="MaxPlanPages"/>; in the latter case the result says <c>IsComplete = false</c>.
    /// </summary>
    private async Task<SubscriptionPlanList> ReadPlansAsync(CancellationToken token, CancellationToken callerToken)
    {
        var familyId = "handle:" + _settings.ProductFamilyHandle!.Trim();
        var plans = new List<SubscriptionPlan>();
        var isComplete = false;

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = familyId,
                        Page = page,
                        PerPage = PlanPageSize
                    },
                    cancellationToken: token);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex) when (ex.Error.TryGetString(out _))
            {
                // 404: the configured product family does not exist on this site — our configuration, not the caller.
                _logger.LogError("Maxio product family configured in {SettingKey} was not found (HTTP {Status}).",
                    $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}", (int)ex.StatusCode);
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "The subscription plan catalog is not available.", innerException: ex);
            }
            catch (Exception ex) when (ShouldTranslate(ex, callerToken))
            {
                throw Translate("ListProductsForProductFamily", ex);
            }

            if (products is null)
            {
                isComplete = true;
                break;
            }

            foreach (var item in products)
            {
                var product = item?.Product;
                if (product is null || product.ArchivedAt is not null || string.IsNullOrWhiteSpace(product.Handle))
                {
                    continue;
                }

                plans.Add(new SubscriptionPlan(
                    product.Handle!,
                    product.Name,
                    product.Description,
                    ToMoney(product.PriceInCents),
                    product.Interval,
                    product.IntervalUnit?.Value));
            }

            if (products.Count < PlanPageSize)
            {
                isComplete = true;
                break;
            }
        }

        if (!isComplete)
        {
            _logger.LogWarning("Maxio plan listing stopped at the {MaxPages}-page cap; the plan list is incomplete.", MaxPlanPages);
        }

        return new SubscriptionPlanList(plans, isComplete);
    }

    // ------------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.UnknownPlan, "A plan handle is required.");
        }

        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;

        // Cross-operation invariant: the handle must be one the configured family offers, checked
        // before any claim is taken and before any Maxio write.
        var plans = await ReadPlansAsync(token, cancellationToken);
        var plan = plans.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.Ordinal));
        if (plan is null)
        {
            if (!plans.IsComplete)
            {
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.CatalogIncomplete,
                    "The plan catalog could not be read completely, so the requested plan could not be verified.");
            }

            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.UnknownPlan,
                $"'{planHandle}' is not an available subscription plan.");
        }

        // Two passes: the second runs only when the first found an abandoned claim and released it.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claim = await TryClaimAsync(subscriber.UserName, plan.Handle);
            if (claim is not null)
            {
                var familyHandles = plans.Plans.Select(p => p.Handle).ToHashSet(StringComparer.Ordinal);
                return await EnrollAsync(claim, plan, familyHandles, subscriber, token, cancellationToken);
            }

            var existing = await _db.SubscriptionEnrollments.FindAsync(new object[] { subscriber.UserName }, CancellationToken.None);
            if (existing is null)
            {
                continue; // released between our insert and our read — claim again
            }

            var resolved = await ResolveExistingClaimAsync(existing, plan, token, cancellationToken);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        throw new SubscriptionBillingException(SubscriptionBillingErrorKind.SubscriptionInProgress,
            "A subscription request for this account is already in progress.");
    }

    /// <summary>
    /// Takes the claim: inserts the enrollment row keyed by user name. The store refuses a second row
    /// for the same user, and that refusal is what stops a second subscribe reaching Maxio.
    /// Returns null when the claim is already held.
    /// </summary>
    private async Task<SubscriptionEnrollment?> TryClaimAsync(string userName, string planHandle)
    {
        var claim = new SubscriptionEnrollment(userName, planHandle, BuildSubscriptionReference(userName), _timeProvider.GetUtcNow());
        _db.SubscriptionEnrollments.Add(claim);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
            return claim;
        }
        catch (Exception ex) when (IsDuplicateClaim(ex))
        {
            _db.Entry(claim).State = EntityState.Detached;
            return null;
        }
    }

    /// <summary>
    /// What the store raises when the claim's primary key is already taken: SQL Server reports a
    /// <see cref="DbUpdateException"/> wrapping a duplicate-key <see cref="SqlException"/> (2627/2601);
    /// the EF Core in-memory provider (UseOnlyInMemoryDatabase) throws <see cref="ArgumentException"/>
    /// from SaveChanges — proven by the InMemoryProvider_RefusesSecondClaimForSameUser unit test.
    /// </summary>
    private bool IsDuplicateClaim(Exception ex) =>
        ex is DbUpdateException { InnerException: SqlException { Number: 2627 or 2601 } }
        || (ex is ArgumentException && _db.Database.IsInMemory());

    private async Task<SubscribeResult?> ResolveExistingClaimAsync(
        SubscriptionEnrollment existing,
        SubscriptionPlan plan,
        CancellationToken token,
        CancellationToken callerToken)
    {
        switch (existing.Status)
        {
            case SubscriptionEnrollmentStatus.Active:
            {
                EnsureSamePlan(existing, plan);
                var current = await TryFindSubscriptionForReplayAsync(existing.SubscriptionReference, token, callerToken);
                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed,
                    current ?? new SubscriptionSummary(existing.BillingSubscriptionId, existing.PlanHandle, plan.Name, plan.Price, null, null, null));
            }

            case SubscriptionEnrollmentStatus.Claimed when !IsPastLease(existing):
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.SubscriptionInProgress,
                    "A subscription request for this account is already in progress.");

            default:
            {
                // Unknown, or a claim abandoned past its lease: settle from what Maxio holds.
                var settled = await SettleAsync(existing);
                if (settled.Subscription is not null)
                {
                    EnsureSamePlan(existing, plan);
                    return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, settled.Subscription);
                }

                if (settled.Outcome == SettleOutcome.NotFound && IsPastLease(existing))
                {
                    // Nothing landed at Maxio and the lease is over: release so this caller can retry.
                    await ReleaseAsync(existing);
                    return null;
                }

                return new SubscribeResult(SubscribeOutcome.PendingConfirmation, null);
            }
        }
    }

    private async Task<SubscribeResult> EnrollAsync(
        SubscriptionEnrollment claim,
        SubscriptionPlan plan,
        IReadOnlySet<string> familyHandles,
        Subscriber subscriber,
        CancellationToken token,
        CancellationToken callerToken)
    {
        int customerId;
        Subscription? existingAtMaxio;
        try
        {
            customerId = await EnsureCustomerAsync(subscriber, token, callerToken);
            claim.RecordCustomer(customerId, _timeProvider.GetUtcNow());
            await _db.SaveChangesAsync(CancellationToken.None);

            // The local claim does not outlive the in-memory store (UseOnlyInMemoryDatabase), so
            // Maxio is checked before every create: a user who subscribed before a restart must not
            // get a second subscription. A failed read here is an error, never a reason to create.
            existingAtMaxio = await FindExistingSubscriptionAsync(claim.SubscriptionReference, customerId, plan.Handle, familyHandles, token, callerToken);
        }
        catch
        {
            // No subscription write was attempted: release so the user can retry.
            await ReleaseAsync(claim);
            throw;
        }

        if (existingAtMaxio is not null)
        {
            var heldPlan = existingAtMaxio.Product?.Handle ?? plan.Handle;
            claim.MarkActive(existingAtMaxio.Id, _timeProvider.GetUtcNow(), heldPlan);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Subscriber {Subscriber} already holds Maxio subscription {SubscriptionId}; no new subscription created.",
                SubscriberTag(subscriber.UserName), existingAtMaxio.Id);

            if (!string.Equals(heldPlan, plan.Handle, StringComparison.Ordinal))
            {
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.AlreadySubscribedToOtherPlan,
                    $"This account already has a subscription to plan '{heldPlan}'.");
            }

            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, ToSummary(existingAtMaxio, plan));
        }

        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = claim.SubscriptionReference,
                            // Design constant, not configuration: subscribers have no payment method on
                            // file, and the default (automatic) collection tries to charge the first
                            // period at signup, which Maxio refuses with 422 "No payment method was on
                            // file". Remittance bills the subscriber by invoice instead. Per the SDK docs
                            // it is the Relationship Invoicing option; legacy Statements-architecture
                            // sites take CollectionMethod.Invoice.
                            PaymentCollectionMethod = SubscriptionCollectionMethod
                        }
                    }
                },
                cancellationToken: token);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var rejected))
            {
                // 422: Maxio refused the subscription — definitive, release the claim.
                var errors = rejected.Errors ?? Array.Empty<string>();
                _logger.LogWarning("Maxio rejected CreateSubscription (HTTP {Status}) for subscriber {Subscriber}: {Errors}",
                    (int)ex.StatusCode, SubscriberTag(subscriber.UserName), string.Join("; ", errors));
                await ReleaseAsync(claim);
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.RejectedByProvider,
                    "The billing provider rejected the subscription.", errors, ex);
            }

            if (ex.Error.TryGetRawError(out RawError raw) && (int)raw.StatusCode < 500)
            {
                // Any other 4xx is a refusal: nothing was created.
                await ReleaseAsync(claim);
                throw Translate("CreateSubscription", ex);
            }

            // 5xx: the provider may have acted before failing.
            return await SettleAfterAmbiguousCreateAsync(claim, plan, ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            await ReleaseAsync(claim);
            throw Translate("CreateSubscription", ex);
        }
        catch (ResponseDeserializationException ex)
        {
            // 2xx we could not read (or an unreadable 5xx): it may well have succeeded.
            return await SettleAfterAmbiguousCreateAsync(claim, plan, ex);
        }
        catch (AuthSchemeException ex)
        {
            // The credential could not be applied, so nothing was sent.
            await ReleaseAsync(claim);
            throw Translate("CreateSubscription", ex);
        }
        catch (SdkConnectionException ex)
        {
            // Includes SdkTimeoutException: the request may have reached Maxio before the connection failed.
            return await SettleAfterAmbiguousCreateAsync(claim, plan, ex);
        }
        catch (OperationCanceledException ex)
        {
            // Budget or caller cancellation after the request may have been sent.
            return await SettleAfterAmbiguousCreateAsync(claim, plan, ex);
        }

        var subscription = response?.Subscription;
        if (subscription is null)
        {
            return await SettleAfterAmbiguousCreateAsync(claim, plan, null);
        }

        claim.MarkActive(subscription.Id, _timeProvider.GetUtcNow());
        await _db.SaveChangesAsync(CancellationToken.None);
        return new SubscribeResult(SubscribeOutcome.Created, ToSummary(subscription, plan));
    }

    /// <summary>
    /// Settles a CreateSubscription whose outcome is unknown by re-reading Maxio for the reference the
    /// claim sent. Found: recorded as active. Not found or unreadable: left <c>Unknown</c> on the claim
    /// (never released here) and reported as pending; the next subscribe or list call settles it again.
    /// </summary>
    private async Task<SubscribeResult> SettleAfterAmbiguousCreateAsync(SubscriptionEnrollment claim, SubscriptionPlan plan, Exception? cause)
    {
        _logger.LogWarning(cause, "Maxio CreateSubscription outcome is unknown for subscriber {Subscriber}; settling by subscription reference.",
            SubscriberTag(claim.UserName));

        var settled = await SettleAsync(claim);
        if (settled.Subscription is not null)
        {
            return new SubscribeResult(SubscribeOutcome.Created, settled.Subscription with
            {
                PlanHandle = settled.Subscription.PlanHandle ?? plan.Handle,
                PlanName = settled.Subscription.PlanName ?? plan.Name
            });
        }

        _logger.LogError("Maxio CreateSubscription outcome for subscriber {Subscriber} is still unknown ({Outcome}); left pending.",
            SubscriberTag(claim.UserName), settled.Outcome);
        return new SubscribeResult(SubscribeOutcome.PendingConfirmation, null);
    }

    private enum SettleOutcome { Found, NotFound, ReadFailed }

    private sealed record SettleResult(SettleOutcome Outcome, SubscriptionSummary? Subscription);

    /// <summary>
    /// Re-reads Maxio for the claim's subscription reference: <c>FindSubscription</c>, then the
    /// customer's subscription list. Uses its own budget, independent of the request's (which may be spent).
    /// </summary>
    private async Task<SettleResult> SettleAsync(SubscriptionEnrollment claim)
    {
        using var settleBudget = new CancellationTokenSource(SettleBudget, _timeProvider);
        var token = settleBudget.Token;

        Subscription? found;
        try
        {
            found = await FindSubscriptionByReferenceAsync(claim.SubscriptionReference, token);
            if (found is null && claim.BillingCustomerId is int customerId)
            {
                var all = await _client.Customers.ListCustomerSubscriptions(
                    new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                    cancellationToken: token);
                found = all?
                    .Select(r => r?.Subscription)
                    .FirstOrDefault(s => s is not null && string.Equals(s.Reference, claim.SubscriptionReference, StringComparison.Ordinal));
            }
        }
        catch (Exception ex) when (ex is SdkException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Re-reading Maxio to settle subscriber {Subscriber} failed; the outcome stays unknown.",
                SubscriberTag(claim.UserName));
            await MarkUnknownAsync(claim);
            return new SettleResult(SettleOutcome.ReadFailed, null);
        }

        if (found is null)
        {
            await MarkUnknownAsync(claim);
            return new SettleResult(SettleOutcome.NotFound, null);
        }

        claim.MarkActive(found.Id, _timeProvider.GetUtcNow());
        await _db.SaveChangesAsync(CancellationToken.None);
        return new SettleResult(SettleOutcome.Found, ToSummary(found, null));
    }

    /// <summary>FindSubscription by reference; null on its documented 404. Other failures propagate.</summary>
    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: token);
            return response?.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
    }

    /// <summary>
    /// Pre-create reconciliation: (a) <c>FindSubscription</c> by the deterministic reference, then
    /// (b) the customer's subscriptions, looking for a live one on a plan of the configured family
    /// (preferring the requested plan). Returns null only when Maxio definitively holds none;
    /// any read failure is translated and thrown.
    /// </summary>
    private async Task<Subscription?> FindExistingSubscriptionAsync(
        string reference,
        int customerId,
        string requestedPlanHandle,
        IReadOnlySet<string> familyHandles,
        CancellationToken token,
        CancellationToken callerToken)
    {
        try
        {
            var byReference = await FindSubscriptionByReferenceAsync(reference, token);
            if (byReference is not null && IsLive(byReference))
            {
                return byReference;
            }

            var all = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId },
                cancellationToken: token);

            var live = (all ?? Array.Empty<SubscriptionResponse>())
                .Select(r => r?.Subscription)
                .Where(s => s is not null
                            && IsLive(s)
                            && s.Product?.Handle is string handle
                            && familyHandles.Contains(handle))
                .Select(s => s!)
                .ToList();

            return live.FirstOrDefault(s => string.Equals(s.Product!.Handle, requestedPlanHandle, StringComparison.Ordinal))
                   ?? live.FirstOrDefault();
        }
        catch (Exception ex) when (ShouldTranslate(ex, callerToken))
        {
            throw Translate("FindExistingSubscription", ex);
        }
    }

    /// <summary>
    /// A subscription still counts as held unless Maxio reports it ended or never started.
    /// Open-enum safe: a state this SDK does not declare (or none) counts as live, so it is never
    /// silently duplicated.
    /// </summary>
    private static bool IsLive(Subscription subscription) =>
        subscription.State is not { } state
        || !(state == SubscriptionState.Canceled
             || state == SubscriptionState.Expired
             || state == SubscriptionState.FailedToCreate);

    private async Task<SubscriptionSummary?> TryFindSubscriptionForReplayAsync(string reference, CancellationToken token, CancellationToken callerToken)
    {
        try
        {
            var subscription = await FindSubscriptionByReferenceAsync(reference, token);
            return subscription is null ? null : ToSummary(subscription, null);
        }
        catch (Exception ex) when (ShouldTranslate(ex, callerToken))
        {
            throw Translate("FindSubscription", ex);
        }
    }

    private static void EnsureSamePlan(SubscriptionEnrollment existing, SubscriptionPlan requested)
    {
        if (!string.Equals(existing.PlanHandle, requested.Handle, StringComparison.Ordinal))
        {
            throw new SubscriptionBillingException(SubscriptionBillingErrorKind.AlreadySubscribedToOtherPlan,
                $"This account already has a subscription to plan '{existing.PlanHandle}'.");
        }
    }

    private bool IsPastLease(SubscriptionEnrollment enrollment) =>
        _timeProvider.GetUtcNow() - enrollment.ClaimedAtUtc >= ClaimLease;

    private async Task MarkUnknownAsync(SubscriptionEnrollment claim)
    {
        if (claim.Status == SubscriptionEnrollmentStatus.Active)
        {
            return;
        }

        claim.MarkUnknown(_timeProvider.GetUtcNow());
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(SubscriptionEnrollment claim)
    {
        try
        {
            _db.SubscriptionEnrollments.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released by a concurrent caller.
            _db.Entry(claim).State = EntityState.Detached;
        }
    }

    // ------------------------------------------------------------------ customer

    /// <summary>
    /// Returns the Maxio customer id for the subscriber, creating the customer when none exists for
    /// its reference (the eShop user name). Maxio allows one customer per reference, and every
    /// ambiguous or refused create is settled by re-reading that reference.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(Subscriber subscriber, CancellationToken token, CancellationToken callerToken)
    {
        var reference = subscriber.UserName;
        var existing = await TryReadCustomerIdAsync(reference, token, callerToken);
        if (existing is int existingId)
        {
            return existingId;
        }

        var email = string.IsNullOrWhiteSpace(subscriber.Email) ? subscriber.UserName : subscriber.Email;
        var at = email.IndexOf('@');
        var firstName = at > 0 ? email[..at] : email;

        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = "eShop",
                            Email = email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: token);

            if (created?.Customer?.Id is int createdId)
            {
                return createdId;
            }

            return await SettleCustomerAsync(reference, null);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var body))
            {
                // 422 — possibly because a concurrent request already created the customer for this reference.
                var again = await TryReadCustomerIdAsync(reference, token, callerToken);
                if (again is int againId)
                {
                    return againId;
                }

                var errors = ExtractErrors(body);
                _logger.LogWarning("Maxio rejected CreateCustomer (HTTP {Status}) for subscriber {Subscriber}: {Errors}",
                    (int)ex.StatusCode, SubscriberTag(subscriber.UserName), string.Join("; ", errors));
                throw new SubscriptionBillingException(SubscriptionBillingErrorKind.RejectedByProvider,
                    "The billing provider rejected the customer record.", errors, ex);
            }

            if (ex.Error.TryGetRawError(out RawError raw) && (int)raw.StatusCode >= 500)
            {
                return await SettleCustomerAsync(reference, ex);
            }

            throw Translate("CreateCustomer", ex);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is < 400 or >= 500)
        {
            return await SettleCustomerAsync(reference, ex);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleCustomerAsync(reference, ex);
        }
        catch (OperationCanceledException ex) when (!callerToken.IsCancellationRequested)
        {
            return await SettleCustomerAsync(reference, ex);
        }
        catch (Exception ex) when (ShouldTranslate(ex, callerToken))
        {
            throw Translate("CreateCustomer", ex);
        }
    }

    /// <summary>
    /// Settles a CreateCustomer whose outcome is unknown by re-reading the customer by reference.
    /// Still not found: the error is reported, which is safe because no subscription write has been
    /// made and Maxio admits only one customer per reference.
    /// </summary>
    private async Task<int> SettleCustomerAsync(string reference, Exception? cause)
    {
        _logger.LogWarning(cause, "Maxio CreateCustomer outcome is unknown for subscriber {Subscriber}; re-reading by reference.",
            SubscriberTag(reference));

        using var settleBudget = new CancellationTokenSource(SettleBudget, _timeProvider);
        int? id;
        try
        {
            id = await TryReadCustomerIdAsync(reference, settleBudget.Token, CancellationToken.None);
        }
        catch (SubscriptionBillingException ex)
        {
            throw new SubscriptionBillingException(ex.Kind == SubscriptionBillingErrorKind.ProviderTimeout
                    ? SubscriptionBillingErrorKind.ProviderTimeout
                    : SubscriptionBillingErrorKind.ProviderUnavailable,
                "The billing customer could not be confirmed. Please try again.", innerException: ex);
        }

        return id ?? throw new SubscriptionBillingException(
            cause is SdkTimeoutException or OperationCanceledException
                ? SubscriptionBillingErrorKind.ProviderTimeout
                : SubscriptionBillingErrorKind.ProviderUnavailable,
            "The billing customer could not be confirmed. Please try again.", innerException: cause);
    }

    /// <summary>ReadCustomerByReference; null on 404 (not found). Any other failure is translated.</summary>
    private async Task<int?> TryReadCustomerIdAsync(string reference, CancellationToken token, CancellationToken callerToken)
    {
        CustomerResponse response;
        try
        {
            response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: token);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ShouldTranslate(ex, callerToken))
        {
            throw Translate("ReadCustomerByReference", ex);
        }

        if (response?.Customer?.Id is int id)
        {
            return id;
        }

        throw new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
            "The billing provider returned a customer without an id.");
    }

    private static IReadOnlyList<string> ExtractErrors(CustomerErrorResponse1 body)
    {
        if (body.Errors is null)
        {
            return Array.Empty<string>();
        }

        if (body.Errors.TryGetListOfString(out var list) && list is not null)
        {
            return list;
        }

        if (body.Errors.TryGetCustomerError(out var customerError) && !string.IsNullOrWhiteSpace(customerError?.Customer))
        {
            return new[] { customerError.Customer! };
        }

        return Array.Empty<string>();
    }

    // ------------------------------------------------------------------ my subscriptions

    public async Task<MySubscriptions> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;

        var pending = false;
        var enrollment = await _db.SubscriptionEnrollments.FindAsync(new object[] { subscriber.UserName }, CancellationToken.None);
        if (enrollment is not null)
        {
            if (enrollment.Status == SubscriptionEnrollmentStatus.Unknown
                || (enrollment.Status == SubscriptionEnrollmentStatus.Claimed && IsPastLease(enrollment)))
            {
                var settled = await SettleAsync(enrollment);
                if (settled.Outcome == SettleOutcome.NotFound && IsPastLease(enrollment))
                {
                    await ReleaseAsync(enrollment);
                }
                else
                {
                    pending = settled.Outcome != SettleOutcome.Found;
                }
            }
            else if (enrollment.Status == SubscriptionEnrollmentStatus.Claimed)
            {
                pending = true; // a subscribe call is in flight
            }
        }

        var customerId = await TryReadCustomerIdAsync(subscriber.UserName, token, cancellationToken);
        if (customerId is not int id)
        {
            return new MySubscriptions(Array.Empty<SubscriptionSummary>(), pending);
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = id },
                cancellationToken: token);
        }
        catch (Exception ex) when (ShouldTranslate(ex, cancellationToken))
        {
            throw Translate("ListCustomerSubscriptions", ex);
        }

        var subscriptions = (responses ?? Array.Empty<SubscriptionResponse>())
            .Select(r => r?.Subscription)
            .Where(s => s is not null)
            .Select(s => ToSummary(s!, null))
            .ToList();

        return new MySubscriptions(subscriptions, pending);
    }

    // ------------------------------------------------------------------ shared

    private CancellationTokenSource CreateBudget(CancellationToken callerToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        budget.CancelAfter(RequestBudget);
        return budget;
    }

    /// <summary>
    /// True for every failure the SDK raises, and for a budget expiry. The caller's own cancellation is
    /// never translated: it propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    private static bool ShouldTranslate(Exception ex, CancellationToken callerToken) =>
        ex is SdkException || (ex is OperationCanceledException && !callerToken.IsCancellationRequested);

    /// <summary>The single SDK-failure → caller-safe error ladder. Never echoes SDK messages.</summary>
    private SubscriptionBillingException Translate(string operation, Exception ex)
    {
        switch (ex)
        {
            case SdkTimeoutException:
                _logger.LogWarning(ex, "Maxio {Operation} timed out.", operation);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderTimeout,
                    "The billing provider did not respond in time.", innerException: ex);

            case SdkConnectionException:
                _logger.LogWarning(ex, "Maxio {Operation} could not reach the provider.", operation);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
                    "The billing provider could not be reached.", innerException: ex);

            case AuthSchemeException:
                _logger.LogError(ex, "Maxio {Operation}: credentials could not be applied.", operation);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "The billing provider is not available.", innerException: ex);

            case ResponseDeserializationException rde:
                _logger.LogError(ex, "Maxio {Operation} returned an unreadable body (HTTP {Status}).", operation, (int)rde.StatusCode);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "The billing provider returned a response that could not be processed.", innerException: ex);

            case ApiException api:
            {
                var status = (int)api.StatusCode;
                switch (status)
                {
                    case 401 or 403:
                        _logger.LogError("Maxio {Operation} rejected this store's credentials (HTTP {Status}). Check {SettingKey}.",
                            operation, status, $"{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}");
                        return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                            "The billing provider is not available.", innerException: ex);
                    case 429 or 503:
                        _logger.LogWarning("Maxio {Operation} is throttling or unavailable (HTTP {Status}).", operation, status);
                        return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderUnavailable,
                            "The billing provider is temporarily unavailable.", innerException: ex);
                    case 422:
                        _logger.LogWarning("Maxio {Operation} rejected the request (HTTP {Status}).", operation, status);
                        return new SubscriptionBillingException(SubscriptionBillingErrorKind.RejectedByProvider,
                            "The billing provider rejected the request.", innerException: ex);
                    default:
                        _logger.LogError("Maxio {Operation} failed (HTTP {Status}).", operation, status);
                        return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                            "The billing provider returned an error.", innerException: ex);
                }
            }

            case OperationCanceledException:
                _logger.LogWarning("Maxio {Operation} did not complete within the {Budget} request budget.", operation, RequestBudget);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderTimeout,
                    "The billing provider did not respond in time.", innerException: ex);

            default:
                _logger.LogError(ex, "Maxio {Operation} failed unexpectedly.", operation);
                return new SubscriptionBillingException(SubscriptionBillingErrorKind.ProviderError,
                    "The billing provider returned an error.", innerException: ex);
        }
    }

    private static SubscriptionSummary ToSummary(Subscription subscription, SubscriptionPlan? plan) =>
        new(
            subscription.Id,
            subscription.Product?.Handle ?? plan?.Handle,
            subscription.Product?.Name ?? plan?.Name,
            ToMoney(subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents) ?? plan?.Price,
            subscription.State?.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            subscription.CreatedAt);

    private static decimal? ToMoney(long? cents) => cents is long c ? c / 100m : null;

    /// <summary>The deterministic subscription reference sent on every create attempt for this user.</summary>
    public static string BuildSubscriptionReference(string userName) => SubscriptionReferencePrefix + userName;

    /// <summary>A non-reversible tag for logs, so user names and e-mails never reach them.</summary>
    private static string SubscriberTag(string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }
}
