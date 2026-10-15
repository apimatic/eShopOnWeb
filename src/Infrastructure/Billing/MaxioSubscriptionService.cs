using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RawError = MaxioAdvancedBilling.Core.ErrorResponse.RawError;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscribes eShopOnWeb users to Maxio Advanced Billing plans. Maxio is the system of record; the local
/// <see cref="SubscriptionClaim"/> table exists to stop a second concurrent subscribe reaching Maxio.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionBillingService
{
    // Budget for one service call (all SDK calls inside it share the one token).
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    // Budget for settling an unknown outcome after the call budget may already be spent.
    private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);
    // A Pending claim older than this is assumed abandoned (crashed process) and is reconciled.
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);
    private const int PlanPageSize = 200;
    private const int MaxPlanPages = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default) =>
        Bounded(ct => FetchPlansAsync(ct), cancellationToken);

    public Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("A user is required.", nameof(userName));
        if (string.IsNullOrWhiteSpace(planHandle)) throw new BillingException(BillingFailure.PlanNotFound, "A plan handle is required.");
        return Bounded(ct => SubscribeCoreAsync(userName, planHandle.Trim(), ct), cancellationToken);
    }

    public Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)) throw new ArgumentException("A user is required.", nameof(userName));
        return Bounded(ct => ListSubscriptionsCoreAsync(userName, ct), cancellationToken);
    }

    // ---- plans -------------------------------------------------------------------------------------------

    private async Task<SubscriptionPlanList> FetchPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var partial = false;

        for (var page = 1; page <= MaxPlanPages; page++)
        {
            var products = await CallAsync("ListProductsForProductFamily", isWrite: false, () =>
                _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = $"handle:{_settings.ProductFamilyHandle}",
                    Page = page,
                    PerPage = PlanPageSize
                }, cancellationToken: ct));

            foreach (var item in products)
            {
                var p = item.Product;
                if (p?.Handle is null || p.ArchivedAt is not null) continue;
                plans.Add(new SubscriptionPlan(
                    p.Handle, p.Name ?? p.Handle, p.Description, p.PriceInCents ?? 0,
                    p.Interval, p.IntervalUnit?.Value));
            }

            if (products.Count < PlanPageSize) break;
            if (page == MaxPlanPages) partial = true; // a full last page: more may exist
        }

        return new SubscriptionPlanList(plans, partial);
    }

    // ---- subscribe ---------------------------------------------------------------------------------------

    private async Task<SubscribeResult> SubscribeCoreAsync(string userName, string planHandle, CancellationToken ct)
    {
        // Invariant: only a plan the shop offers (the family listing) may be subscribed to.
        var offered = await FetchPlansAsync(ct);
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal))
                   ?? throw new BillingException(BillingFailure.PlanNotFound, $"'{planHandle}' is not an available plan.");

        var reference = SubscriptionReference(userName, plan.Handle);

        // claim -> SDK calls -> record the result
        var (claim, existing) = await ClaimAsync(userName, plan, reference, ct);
        if (existing is not null) return existing;

        int customerId;
        try
        {
            // The local claim table can be empty while Maxio already holds the subscription (data loss, restore,
            // in-memory restart): Maxio is the source of truth, so adopt what it has before writing.
            var held = await FindSubscriptionAsync(reference, ct);
            if (held is not null)
            {
                await RecordActiveAsync(claim, null, held.Id);
                return new SubscribeResult(ToInfo(held, plan), AlreadySubscribed: true);
            }

            customerId = await EnsureCustomerAsync(userName, ct);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            // No subscription write has happened yet: the claim can be released.
            await ReleaseAsync(claim);
            throw;
        }

        try
        {
            var response = await CallAsync("CreateSubscription", isWrite: true, () =>
                _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = reference,
                            // Bill by invoice: no card has to be captured at signup.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                }, cancellationToken: ct));

            var sub = response.Subscription;
            if (sub?.Id is null)
                throw new BillingException(BillingFailure.OutcomeUnknown, "Maxio returned no subscription id.");

            await RecordActiveAsync(claim, customerId, sub.Id);
            _logger.LogInformation("Subscribed user to plan {Plan} as Maxio subscription {SubscriptionId}", plan.Handle, sub.Id);
            return new SubscribeResult(ToInfo(sub, plan), AlreadySubscribed: false);
        }
        catch (Exception ex) when (ex is OperationCanceledException ||
                                   ex is BillingException { Failure: BillingFailure.OutcomeUnknown })
        {
            // The write may have landed: settle by re-reading the subscription by the reference we sent.
            var settled = await SettleAsync(claim, plan, reference, customerId);
            if (settled is not null) return new SubscribeResult(settled, AlreadySubscribed: false);

            await MarkUnknownAsync(claim);
            _logger.LogWarning("Subscription outcome for plan {Plan} is unknown and will be reconciled on the next attempt", plan.Handle);
            if (ex is BillingException) throw;
            throw new BillingException(BillingFailure.OutcomeUnknown, "The subscription outcome could not be confirmed in time.", ex);
        }
        catch (BillingException)
        {
            // Provider definitively refused the write: release the claim so a corrected retry can proceed.
            await ReleaseAsync(claim);
            throw;
        }
    }

    /// <summary>
    /// Inserts the claim row. The composite primary key rejects the second caller; that caller reads the
    /// existing row and either returns the existing subscription, reports "in progress", or reconciles a
    /// stale/unknown claim against Maxio.
    /// </summary>
    private async Task<(SubscriptionClaim Claim, SubscribeResult? Existing)> ClaimAsync(
        string userName, SubscriptionPlan plan, string reference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claim = new SubscriptionClaim(userName, plan.Handle, DateTimeOffset.UtcNow);
            try
            {
                _db.SubscriptionClaims.Add(claim);
                await _db.SaveChangesAsync(ct);
                return (claim, null);
            }
            catch (Exception ex) when (ex is DbUpdateException or ArgumentException or InvalidOperationException)
            {
                // The store refused the duplicate key (the in-memory provider surfaces it as ArgumentException,
                // a context that already tracks the key as InvalidOperationException).
                if (_db.Entry(claim).State != EntityState.Detached) _db.Entry(claim).State = EntityState.Detached;
            }

            var current = await _db.SubscriptionClaims.FirstOrDefaultAsync(
                c => c.BuyerId == userName && c.PlanHandle == plan.Handle, ct);
            if (current is null) continue; // released between our insert and read: claim again

            var inFlight = current.Status == SubscriptionClaimStatus.Pending &&
                           DateTimeOffset.UtcNow - current.ClaimedAt < StaleClaimAge;
            if (inFlight)
                throw new BillingException(BillingFailure.InProgress, "A subscription to this plan is already being created.");

            // Active, Unknown or abandoned-Pending: Maxio is the source of truth.
            var found = await FindSubscriptionAsync(reference, ct);
            if (found is not null)
            {
                if (current.Status != SubscriptionClaimStatus.Active)
                {
                    await RecordActiveAsync(current, current.MaxioCustomerId, found.Id);
                    _logger.LogWarning("Reconciled claim for plan {Plan} against Maxio", plan.Handle);
                }
                return (current, new SubscribeResult(ToInfo(found, plan), AlreadySubscribed: true));
            }

            // Maxio has nothing for this reference: the stale claim is void; drop it and claim afresh.
            await ReleaseAsync(current);
        }

        throw new BillingException(BillingFailure.InProgress, "A subscription to this plan is already being created.");
    }

    private async Task<int> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        var existing = await FindCustomerAsync(userName, ct);
        if (existing?.Id is int existingId) return existingId;

        var (first, last) = SplitName(userName);
        try
        {
            var created = await CallAsync("CreateCustomer", isWrite: true, () =>
                _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = first,
                            LastName = last,
                            Email = userName,
                            Reference = userName
                        }
                    }
                }, cancellationToken: ct));

            return created.Customer.Id
                   ?? throw new BillingException(BillingFailure.OutcomeUnknown, "Maxio returned no customer id.");
        }
        catch (BillingException ex) when (ex.Failure is BillingFailure.OutcomeUnknown or BillingFailure.Rejected)
        {
            // Unknown outcome, or a duplicate-reference rejection from a concurrent subscribe to another plan:
            // look the customer up by the reference we sent before giving up.
            var settled = await FindCustomerAsync(userName, ct);
            if (settled?.Id is int settledId) return settledId;
            throw;
        }
    }

    // ---- my subscriptions --------------------------------------------------------------------------------

    private async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsCoreAsync(string userName, CancellationToken ct)
    {
        var customer = await FindCustomerAsync(userName, ct);
        if (customer?.Id is not int customerId) return Array.Empty<SubscriptionInfo>();

        var subs = await CallAsync("ListCustomerSubscriptions", isWrite: false, () =>
            _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct));

        return subs
            .Where(s => s.Subscription?.Id is not null)
            .Select(s => ToInfo(s.Subscription!, null))
            .ToList();
    }

    // ---- lookups (a 404 is a genuine "not found"; every other failure propagates) ------------------------

    private Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct) =>
        CallAsync("ReadCustomerByReference", isWrite: false, async () =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(
                    new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
                return (Customer?)response.Customer;
            }
            catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        });

    private Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct) =>
        CallAsync("FindSubscription", isWrite: false, async () =>
        {
            try
            {
                var response = await _client.Subscriptions.FindSubscription(
                    new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
                return response.Subscription;
            }
            catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        });

    // ---- unknown-outcome settlement and claim bookkeeping ------------------------------------------------

    private async Task<SubscriptionInfo?> SettleAsync(SubscriptionClaim claim, SubscriptionPlan plan, string reference, int customerId)
    {
        using var cts = new CancellationTokenSource(SettleBudget);
        try
        {
            var found = await FindSubscriptionAsync(reference, cts.Token);
            if (found is null) return null;
            await RecordActiveAsync(claim, customerId, found.Id);
            _logger.LogWarning("Settled unknown subscription outcome for plan {Plan}: subscription exists", plan.Handle);
            return ToInfo(found, plan);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            return null; // still unknown
        }
    }

    private async Task RecordActiveAsync(SubscriptionClaim claim, int? customerId, int? subscriptionId)
    {
        claim.MarkActive(customerId, subscriptionId);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkUnknownAsync(SubscriptionClaim claim)
    {
        claim.MarkUnknown();
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(SubscriptionClaim claim)
    {
        _db.SubscriptionClaims.Remove(claim);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            // Already gone (released by a concurrent reconcile); nothing to do.
            _db.Entry(claim).State = EntityState.Detached;
        }
    }

    // ---- SDK call boundary --------------------------------------------------------------------------------

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(BillingFailure.ProviderUnavailable, "The billing provider did not answer in time.");
        }
    }

    /// <summary>The one place SDK failures become <see cref="BillingException"/>.</summary>
    private async Task<T> CallAsync<T>(string operation, bool isWrite, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException ex)
        {
            // Includes ResponseDeserializationException (the server answered but the body did not fit).
            throw MapApiFailure(operation, isWrite, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio {Operation}: credentials could not be applied", operation);
            throw new BillingException(BillingFailure.ProviderUnavailable, "The billing provider is unavailable.", ex);
        }
        catch (SdkConnectionException ex) // also SdkTimeoutException
        {
            _logger.LogWarning(ex, "Maxio {Operation}: no usable response", operation);
            throw isWrite
                ? new BillingException(BillingFailure.OutcomeUnknown, "The billing provider did not confirm the request.", ex)
                : new BillingException(BillingFailure.ProviderUnavailable, "The billing provider is unavailable.", ex);
        }
    }

    private BillingException MapApiFailure(string operation, bool isWrite, ApiException ex)
    {
        var status = (int)ex.StatusCode;
        var detail = DescribeBody(ex);
        _logger.LogWarning("Maxio {Operation} failed with HTTP {Status}{Detail}", operation, status, detail is null ? "" : ": " + detail);

        // Success status but unreadable body: the write may have been applied.
        if (ex is ResponseDeserializationException && status is >= 200 and < 300)
            return new BillingException(isWrite ? BillingFailure.OutcomeUnknown : BillingFailure.ProviderUnavailable,
                "The billing provider returned a response that could not be processed.", ex);

        // Our credentials or our quota: not the caller's to fix.
        if (status is 401 or 403 or 429)
            return new BillingException(BillingFailure.ProviderUnavailable, "The billing provider is unavailable.", ex);

        if (status is >= 400 and < 500)
            return new BillingException(BillingFailure.Rejected, detail ?? $"The billing provider rejected the request (HTTP {status}).", ex);

        return new BillingException(
            isWrite ? BillingFailure.OutcomeUnknown : BillingFailure.ProviderUnavailable,
            "The billing provider is unavailable.", ex);
    }

    private static string? DescribeBody(ApiException ex) => ex switch
    {
        ApiException<CreateSubscriptionError> e when e.Error.TryGetErrorListResponse1(out var list) =>
            string.Join("; ", list.Errors),
        ApiException<CreateCustomerError> e when e.Error.TryGetCustomerErrorResponse1(out _) =>
            "The customer details were rejected by the billing provider.",
        _ => null
    };

    // ---- mapping -----------------------------------------------------------------------------------------

    private static SubscriptionInfo ToInfo(Subscription sub, SubscriptionPlan? plan) => new(
        sub.Id ?? 0,
        sub.Product?.Handle ?? plan?.Handle,
        sub.Product?.Name ?? plan?.Name,
        sub.State?.Value,
        sub.ProductPriceInCents ?? plan?.PriceInCents ?? 0,
        sub.NextAssessmentAt,
        sub.CurrentPeriodEndsAt);

    private static string SubscriptionReference(string userName, string planHandle) => $"eshop:{userName}:{planHandle}";

    private static (string First, string Last) SplitName(string userName)
    {
        var at = userName.IndexOf('@');
        var local = at > 0 ? userName[..at] : userName;
        return (string.IsNullOrWhiteSpace(local) ? "Shopper" : local, "Shopper");
    }
}
