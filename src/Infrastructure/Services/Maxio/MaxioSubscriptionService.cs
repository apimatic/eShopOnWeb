using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Subscription billing against Maxio Advanced Billing (system of record).
/// Every write follows: claim (primary-key insert in our store) -> SDK call -> record result.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PageSize = 200;
    private const int MaxPages = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, CatalogContext db,
        IOptions<MaxioSettings> settings, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public Task<PlanList> ListPlansAsync(CancellationToken cancellationToken) =>
        Bounded(ct => ReadPlansAsync(ct), cancellationToken);

    private async Task<PlanList> ReadPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        try
        {
            for (var page = 1; ; page++)
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PageSize,
                    }, cancellationToken: ct);

                foreach (var p in products.Select(r => r.Product))
                {
                    if (p?.Handle is null || p.ArchivedAt is not null) continue;
                    plans.Add(new SubscriptionPlan(p.Handle, p.Name ?? p.Handle, p.Description,
                        p.PriceInCents ?? 0, p.Interval, p.IntervalUnit?.Value));
                }

                if (products.Count < PageSize) break;
                if (page >= MaxPages) { truncated = true; break; }
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Translate(ex, "list plans");
        }

        return new PlanList(plans, truncated);
    }

    // ------------------------------------------------------------ subscribe

    public Task<SubscriptionSummary> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken) =>
        Bounded(ct => SubscribeCoreAsync(request, ct), cancellationToken);

    private async Task<SubscriptionSummary> SubscribeCoreAsync(SubscribeRequest request, CancellationToken ct)
    {
        var planHandle = (request.PlanHandle ?? _settings.DefaultPlanHandle)?.Trim();
        if (string.IsNullOrEmpty(planHandle))
            throw new SubscriptionBillingException(SubscriptionFailure.Invalid, "A plan handle is required.");

        // Invariant: the plan must be one the configured product family offers.
        var offered = await ReadPlansAsync(ct);
        if (offered.IsTruncated)
            throw new SubscriptionBillingException(SubscriptionFailure.Unavailable,
                "The plan catalog is too large to validate the requested plan.");
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
            throw new SubscriptionBillingException(SubscriptionFailure.Invalid,
                $"Plan '{planHandle}' is not offered.");

        var customerId = await EnsureCustomerAsync(request, ct);
        return await EnsureSubscriptionAsync(request.UserKey, customerId, plan, ct);
    }

    private async Task<int> EnsureCustomerAsync(SubscribeRequest request, CancellationToken ct)
    {
        var userKey = request.UserKey;

        var claim = new MaxioCustomerLink { UserKey = userKey, Status = ClaimStatus.Pending };
        try
        {
            _db.MaxioCustomerLinks.Add(claim);
            await _db.SaveChangesAsync(ct);   // the claim: the primary key refuses a second one
        }
        catch (Exception ex) when (IsDuplicateClaim(ex))
        {
            if (_db.Entry(claim).State == EntityState.Added) _db.Entry(claim).State = EntityState.Detached;
            claim = await _db.MaxioCustomerLinks.FirstAsync(l => l.UserKey == userKey, ct);
            if (claim.Status == ClaimStatus.Ready && claim.MaxioCustomerId is int id)
                return id;
            if (claim.Status == ClaimStatus.Pending)
                throw new SubscriptionBillingException(SubscriptionFailure.Conflict,
                    "A subscription request for this user is already in progress.");
            // Unknown: a previous attempt may have created the customer; the lookup below settles it.
            claim.Status = ClaimStatus.Pending;
            await _db.SaveChangesAsync(ct);
        }

        try
        {
            var found = await FindCustomerAsync(userKey, ct);
            if (found is not null)
            {
                _logger.LogInformation("Maxio customer {CustomerId} found by reference", found);
                return await MarkCustomerReadyAsync(claim, found.Value);
            }

            int created;
            try
            {
                var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = request.FirstName ?? "Customer",
                            LastName = request.LastName ?? "Customer",
                            Email = request.Email,
                            Reference = userKey,
                        }
                    }
                }, cancellationToken: ct);
                created = response.Customer?.Id
                    ?? throw new SubscriptionBillingException(SubscriptionFailure.Unavailable,
                        "The billing system returned no customer id.");
            }
            catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode == 422)
            {
                await ReleaseAsync(claim);
                _logger.LogWarning("Maxio refused customer create ({Status}); claim released", (int)ex.StatusCode);
                throw new SubscriptionBillingException(SubscriptionFailure.Rejected,
                    "The billing system rejected the customer details.", ex);
            }
            catch (Exception ex) when (IsAmbiguousWrite(ex))
            {
                // The write may have landed: settle by re-reading with the same reference.
                var settled = await TryFindCustomerAsync(userKey, ct);
                if (settled.Outcome == Settle.Found)
                {
                    _logger.LogWarning("Maxio customer create outcome was ambiguous; settled as created");
                    return await MarkCustomerReadyAsync(claim, settled.Id!.Value);
                }
                if (settled.Outcome == Settle.Absent) await ReleaseAsync(claim);
                else await MarkUnknownAsync(claim);
                throw Translate(ex, "create customer");
            }

            _logger.LogInformation("Maxio customer {CustomerId} created", created);
            return await MarkCustomerReadyAsync(claim, created);
        }
        catch (SubscriptionBillingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await MarkUnknownAsync(claim);
            throw;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            await ReleaseAsync(claim);
            throw Translate(ex, "ensure customer");
        }
    }

    private async Task<SubscriptionSummary> EnsureSubscriptionAsync(string userKey, int customerId,
        SubscriptionPlan plan, CancellationToken ct)
    {
        var reference = $"eshop:{userKey}:{plan.Handle}";
        var claim = new MaxioSubscriptionLink
        {
            UserKey = userKey, PlanHandle = plan.Handle, Reference = reference, Status = ClaimStatus.Pending
        };
        try
        {
            _db.MaxioSubscriptionLinks.Add(claim);
            await _db.SaveChangesAsync(ct);   // the claim: the composite primary key refuses a second one
        }
        catch (Exception ex) when (IsDuplicateClaim(ex))
        {
            if (_db.Entry(claim).State == EntityState.Added) _db.Entry(claim).State = EntityState.Detached;
            claim = await _db.MaxioSubscriptionLinks.FirstAsync(
                l => l.UserKey == userKey && l.PlanHandle == plan.Handle, ct);
            if (claim.Status == ClaimStatus.Pending)
                throw new SubscriptionBillingException(SubscriptionFailure.Conflict,
                    "A subscription request for this plan is already in progress.");

            // Ready or Unknown: read what Maxio holds for our reference.
            var settled = await TryFindSubscriptionAsync(reference, ct);
            if (settled.Subscription is not null)
            {
                await MarkSubscriptionReadyAsync(claim, settled.Subscription);
                return ToSummary(settled.Subscription);
            }
            if (settled.Outcome != Settle.Absent)
                throw new SubscriptionBillingException(SubscriptionFailure.Unavailable,
                    "The existing subscription could not be confirmed with the billing system.");

            // Maxio holds nothing for this reference (a never-landed Unknown): take the claim over.
            claim.Status = ClaimStatus.Pending;
            claim.MaxioSubscriptionId = null;
            await _db.SaveChangesAsync(ct);
        }

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        Reference = reference,
                        // Plans need no card on file: bill by invoice (remittance) rather than automatic capture.
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    }
                }
            }, cancellationToken: ct);

            var sub = response.Subscription
                ?? throw new SubscriptionBillingException(SubscriptionFailure.Unavailable,
                    "The billing system returned no subscription.");
            await MarkSubscriptionReadyAsync(claim, sub);
            _logger.LogInformation("Maxio subscription {SubscriptionId} created for plan {Plan} in state {State}",
                sub.Id, plan.Handle, sub.State?.Value);
            return ToSummary(sub);
        }
        catch (SubscriptionBillingException)
        {
            throw;
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode == 422)
        {
            await ReleaseAsync(claim);
            var detail = ex.Error.TryGetErrorListResponse1(out var list)
                ? string.Join("; ", list.Errors) : "request rejected";
            _logger.LogWarning("Maxio refused subscription create ({Status}); claim released", (int)ex.StatusCode);
            throw new SubscriptionBillingException(SubscriptionFailure.Rejected,
                $"The billing system rejected the subscription: {detail}", ex);
        }
        catch (Exception ex) when (IsAmbiguousWrite(ex))
        {
            var settled = await TryFindSubscriptionAsync(reference, ct);
            if (settled.Subscription is not null)
            {
                _logger.LogWarning("Maxio subscription create outcome was ambiguous; settled as created");
                await MarkSubscriptionReadyAsync(claim, settled.Subscription);
                return ToSummary(settled.Subscription);
            }
            if (settled.Outcome == Settle.Absent) await ReleaseAsync(claim);
            else await MarkUnknownAsync(claim);
            throw Translate(ex, "create subscription");
        }
        catch (OperationCanceledException)
        {
            await MarkUnknownAsync(claim);
            throw;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            await ReleaseAsync(claim);
            throw Translate(ex, "create subscription");
        }
    }

    // -------------------------------------------------------- my subscriptions

    public Task<SubscriptionList> ListMySubscriptionsAsync(string userKey, CancellationToken cancellationToken) =>
        Bounded(ct => ListMineCoreAsync(userKey, ct), cancellationToken);

    private async Task<SubscriptionList> ListMineCoreAsync(string userKey, CancellationToken ct)
    {
        try
        {
            var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserKey == userKey, ct);
            int? customerId = link?.Status == ClaimStatus.Ready ? link.MaxioCustomerId : null;
            // Link lost (e.g. in-memory store restarted): the customer is recoverable by reference.
            customerId ??= await FindCustomerAsync(userKey, ct);
            if (customerId is null) return new SubscriptionList(Array.Empty<SubscriptionSummary>(), false);

            var result = new List<SubscriptionSummary>();
            var truncated = false;
            for (var page = 1; ; page++)
            {
                var batch = await _client.Subscriptions.ListSubscriptions(new ListSubscriptionsRequest
                {
                    CustomerId = customerId,
                    Page = page,
                    PerPage = PageSize,
                }, cancellationToken: ct);
                result.AddRange(batch.Where(b => b.Subscription is not null).Select(b => ToSummary(b.Subscription!)));
                if (batch.Count < PageSize) break;
                if (page >= MaxPages) { truncated = true; break; }
            }
            return new SubscriptionList(result, truncated);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Translate(ex, "list subscriptions");
        }
    }

    // ------------------------------------------------------------- lookups

    private enum Settle { Found, Absent, Failed }

    private async Task<int?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer?.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<(Settle Outcome, int? Id)> TryFindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var id = await FindCustomerAsync(reference, ct);
            return id is null ? (Settle.Absent, null) : (Settle.Found, id);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return (Settle.Failed, null);
        }
    }

    private async Task<(Settle Outcome, Subscription? Subscription)> TryFindSubscriptionAsync(
        string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return response.Subscription is null ? (Settle.Absent, null) : (Settle.Found, response.Subscription);
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (Settle.Absent, null);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return (Settle.Failed, null);
        }
    }

    // ------------------------------------------------------- claim bookkeeping

    private async Task<int> MarkCustomerReadyAsync(MaxioCustomerLink claim, int customerId)
    {
        claim.MaxioCustomerId = customerId;
        claim.Status = ClaimStatus.Ready;
        await _db.SaveChangesAsync(CancellationToken.None);
        return customerId;
    }

    private async Task MarkSubscriptionReadyAsync(MaxioSubscriptionLink claim, Subscription sub)
    {
        claim.MaxioSubscriptionId = sub.Id;
        claim.Status = ClaimStatus.Ready;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(MaxioCustomerLink claim)
    {
        _db.MaxioCustomerLinks.Remove(claim);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(MaxioSubscriptionLink claim)
    {
        _db.MaxioSubscriptionLinks.Remove(claim);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkUnknownAsync(MaxioCustomerLink claim)
    {
        claim.Status = ClaimStatus.Unknown;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkUnknownAsync(MaxioSubscriptionLink claim)
    {
        claim.Status = ClaimStatus.Unknown;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------- mapping

    private static SubscriptionSummary ToSummary(Subscription s) => new(
        s.Id,
        s.Product?.Handle,
        s.Product?.Name,
        s.CurrentBillingAmountInCents ?? s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.State?.Value,
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);

    /// <summary>A write whose outcome is unknown: no usable response, or an unreadable 2xx.</summary>
    private static bool IsAmbiguousWrite(Exception ex) =>
        ex is SdkConnectionException
        || (ex is ResponseDeserializationException r && (int)r.StatusCode is >= 200 and < 300);

    /// <summary>
    /// What the store raises when a claim's primary key already exists. EF relational providers raise
    /// DbUpdateException; the in-memory provider surfaces the duplicate key directly.
    /// </summary>
    private static bool IsDuplicateClaim(Exception ex) =>
        ex is DbUpdateException or ArgumentException or InvalidOperationException;

    private static bool IsProviderFailure(Exception ex) => ex is SdkException;

    private SubscriptionBillingException Translate(Exception ex, string operation)
    {
        var status = (ex as ApiException)?.StatusCode;
        _logger.LogError(ex, "Maxio {Operation} failed (status {Status})", operation, (int?)status);

        // 401/403/429 are our credentials/quota, not the caller's fault; unmapped statuses are unknowns.
        if (status is HttpStatusCode s && (int)s is >= 400 and < 500
            && s != HttpStatusCode.Unauthorized && s != HttpStatusCode.Forbidden && (int)s != 429)
            return new SubscriptionBillingException(SubscriptionFailure.Rejected,
                $"The billing system rejected the request ({(int)s}).", ex);

        return new SubscriptionBillingException(SubscriptionFailure.Unavailable,
            "The billing system is temporarily unavailable.", ex);
    }

    /// <summary>One total budget for every call through this service, linked to the caller's cancellation.</summary>
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_settings.RequestBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SubscriptionBillingException(SubscriptionFailure.Unavailable,
                "The billing system did not answer in time.");
        }
    }
}
