using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.MaxioBillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscribe flow against Maxio. Every write follows: claim (local PK insert) -> SDK call -> record result.
/// Unknown outcomes (connection failure, timeout, 5xx, unreadable 2xx) are settled by re-reading Maxio by reference.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const int PlanPageSize = 200;
    private const int MaxPlanPages = 10;
    private static readonly TimeSpan ReconcileBudget = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);

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

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        try
        {
            return await ReadPlansAsync(cts.Token);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "list plans");
        }
    }

    private async Task<SubscriptionPlanList> ReadPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlanInfo>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            var batch = await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                    Page = page,
                    PerPage = PlanPageSize
                }, cancellationToken: ct);

            foreach (var item in batch)
            {
                var p = item?.Product;
                if (p?.Handle is null || p.ArchivedAt is not null) continue;
                plans.Add(new SubscriptionPlanInfo(p.Handle, p.Name, p.Description, p.PriceInCents ?? 0,
                    p.Interval, p.IntervalUnit?.Value));
            }

            if (batch.Count < PlanPageSize) break;
            if (page >= MaxPlanPages) { truncated = true; break; }
        }
        return new SubscriptionPlanList(plans, truncated);
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(string userId, string email, string? planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new MaxioBillingException(MaxioBillingFailure.InvalidRequest, "A signed-in user is required.");
        planHandle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new MaxioBillingException(MaxioBillingFailure.InvalidRequest, "planHandle is required.");

        using var cts = Budget(cancellationToken);
        var ct = cts.Token;
        try
        {
            // Invariant: the plan must be one this application offers (the same list GET /api/subscription-plans shows).
            var plans = await ReadPlansAsync(ct);
            var plan = plans.Plans.FirstOrDefault(p => p.Handle == planHandle);
            if (plan is null)
            {
                if (plans.Truncated)
                    throw new MaxioBillingException(MaxioBillingFailure.ProviderUnavailable,
                        "The plan catalog is too large to validate the requested plan.");
                throw new MaxioBillingException(MaxioBillingFailure.UnknownPlan, $"Plan '{planHandle}' is not available.");
            }

            var customerId = await EnsureCustomerAsync(userId, email, ct);
            return await EnsureSubscriptionAsync(userId, plan, customerId, ct);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "subscribe");
        }
    }

    // ------------------------------------------------------------- customer

    private async Task<int> EnsureCustomerAsync(string userId, string email, CancellationToken ct)
    {
        var link = new MaxioCustomerLink { UserId = userId, Status = MaxioLinkStatus.Claimed, CreatedUtc = DateTime.UtcNow };
        _db.MaxioCustomerLinks.Add(link);
        try
        {
            // CLAIM: the primary key refuses a second claim for the same user.
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _db.Entry(link).State = EntityState.Detached;
            return await ResolveExistingCustomerAsync(userId, ct);
        }

        // SDK CALL
        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = FirstNameFrom(email),
                        LastName = "-",
                        Email = email,
                        Reference = userId
                    }
                }
            }, cancellationToken: ct);

            var id = created.Customer.Id ?? throw new MaxioBillingException(
                MaxioBillingFailure.ProviderUnavailable, "Billing provider returned no customer id.");
            await CompleteCustomerAsync(link, id);
            return id;
        }
        catch (Exception ex) when (IsUnknownOutcome(ex) || IsDuplicateReference(ex))
        {
            // UNKNOWN OUTCOME (or the reference already exists at Maxio): settle by re-reading by reference.
            _logger.LogWarning("CreateCustomer outcome unknown/duplicate for user {UserKey}: {Reason}", Hash(userId), ex.GetType().Name);
            var settled = await ReconcileCustomerAsync(userId);
            if (settled.Id is int found)
            {
                await CompleteCustomerAsync(link, found);
                return found;
            }
            if (settled.Absent)
            {
                await ReleaseAsync(link);
                throw Translate(ex, "create customer");
            }
            // Re-read failed too: stay Claimed (= unknown), settled by the next request.
            throw new MaxioBillingException(MaxioBillingFailure.InProgress,
                "Customer creation is still being confirmed with the billing provider; retry shortly.", ex);
        }
        catch (Exception ex) when (ex is not MaxioBillingException && ex is not OperationCanceledException)
        {
            await ReleaseAsync(link); // definite refusal: release the claim
            throw;
        }
    }

    private async Task<int> ResolveExistingCustomerAsync(string userId, CancellationToken ct)
    {
        var existing = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct);
        if (existing?.MaxioCustomerId is int id && existing.Status == MaxioLinkStatus.Active) return id;

        // Claimed by a concurrent/earlier attempt: try to settle from Maxio, else report in progress.
        var settled = await ReconcileCustomerAsync(userId);
        if (settled.Id is int found)
        {
            await MarkCustomerActiveAsync(userId, found);
            return found;
        }
        var stale = existing is null || DateTime.UtcNow - existing.CreatedUtc > StaleClaimAge;
        if (settled.Absent && stale)
        {
            // Abandoned claim (process died mid-call): delete it so the next attempt can proceed.
            _db.ChangeTracker.Clear();
            var row = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId, ct);
            if (row is not null) { _db.MaxioCustomerLinks.Remove(row); await _db.SaveChangesAsync(ct); }
        }
        throw new MaxioBillingException(MaxioBillingFailure.InProgress,
            "A subscription request for this user is already in progress; retry shortly.");
    }

    private async Task CompleteCustomerAsync(MaxioCustomerLink link, int customerId)
    {
        link.MaxioCustomerId = customerId;
        link.Status = MaxioLinkStatus.Active;
        if (_db.Entry(link).State == EntityState.Detached) _db.Attach(link).State = EntityState.Modified;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkCustomerActiveAsync(string userId, int customerId)
    {
        var row = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId);
        if (row is null) return;
        row.MaxioCustomerId = customerId;
        row.Status = MaxioLinkStatus.Active;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(MaxioCustomerLink link)
    {
        _db.Entry(link).State = EntityState.Deleted;
        try { await _db.SaveChangesAsync(CancellationToken.None); }
        catch (Exception ex) { _logger.LogError(ex, "Could not release customer claim"); _db.Entry(link).State = EntityState.Detached; }
    }

    private async Task<(int? Id, bool Absent)> ReconcileCustomerAsync(string userId)
    {
        using var cts = new CancellationTokenSource(ReconcileBudget);
        try
        {
            var r = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userId }, cancellationToken: cts.Token);
            return (r.Customer?.Id, false);
        }
        catch (ApiException ex) when (ex is not ResponseDeserializationException && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Customer reconcile read failed: {Reason}", ex.GetType().Name);
            return (null, false);
        }
    }

    // --------------------------------------------------------- subscription

    private async Task<SubscribeResult> EnsureSubscriptionAsync(string userId, SubscriptionPlanInfo plan, int customerId, CancellationToken ct)
    {
        var reference = SubscriptionReference(userId, plan.Handle);
        var link = new MaxioSubscriptionLink
        {
            UserId = userId, PlanHandle = plan.Handle, Status = MaxioLinkStatus.Claimed, CreatedUtc = DateTime.UtcNow
        };
        _db.MaxioSubscriptionLinks.Add(link);
        try
        {
            // CLAIM: the composite primary key refuses a second claim for (user, plan).
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _db.Entry(link).State = EntityState.Detached;
            return await ResolveExistingSubscriptionAsync(userId, plan, reference, ct);
        }

        // SDK CALL
        try
        {
            var created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        Reference = reference,
                        PaymentCollectionMethod = CollectionMethodFromSettings()
                    }
                }
            }, cancellationToken: ct);

            var sub = created.Subscription ?? throw new MaxioBillingException(
                MaxioBillingFailure.ProviderUnavailable, "Billing provider returned no subscription.");
            await CompleteSubscriptionAsync(link, sub.Id);
            _logger.LogInformation("Subscribed user {UserKey} to plan {Plan}", Hash(userId), plan.Handle);
            return new SubscribeResult(ToInfo(sub, plan), false);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            _logger.LogWarning("CreateSubscription outcome unknown for user {UserKey}: {Reason}", Hash(userId), ex.GetType().Name);
            var settled = await ReconcileSubscriptionAsync(reference, customerId, plan.Handle);
            if (settled.Sub is not null)
            {
                await CompleteSubscriptionAsync(link, settled.Sub.Id);
                return new SubscribeResult(ToInfo(settled.Sub, plan), false);
            }
            if (settled.Absent)
            {
                await ReleaseAsync(link);
                throw Translate(ex, "create subscription");
            }
            throw new MaxioBillingException(MaxioBillingFailure.InProgress,
                "Subscription is still being confirmed with the billing provider; retry shortly.", ex);
        }
        catch (Exception ex) when (ex is not MaxioBillingException && ex is not OperationCanceledException)
        {
            await ReleaseAsync(link);
            throw;
        }
    }

    private async Task<SubscribeResult> ResolveExistingSubscriptionAsync(string userId, SubscriptionPlanInfo plan, string reference, CancellationToken ct)
    {
        var existing = await _db.MaxioSubscriptionLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanHandle == plan.Handle, ct);
        var customerId = (await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct))?.MaxioCustomerId;

        var settled = await ReconcileSubscriptionAsync(reference, customerId, plan.Handle);
        if (settled.Sub is not null)
        {
            if (existing?.Status != MaxioLinkStatus.Active) await MarkSubscriptionActiveAsync(userId, plan.Handle, settled.Sub.Id);
            return new SubscribeResult(ToInfo(settled.Sub, plan), true);
        }
        if (settled.Absent && existing is not null && DateTime.UtcNow - existing.CreatedUtc > StaleClaimAge)
        {
            // Abandoned claim: release it so the next attempt can proceed.
            _db.ChangeTracker.Clear();
            var row = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserId == userId && l.PlanHandle == plan.Handle, ct);
            if (row is not null) { _db.MaxioSubscriptionLinks.Remove(row); await _db.SaveChangesAsync(ct); }
        }
        throw new MaxioBillingException(MaxioBillingFailure.InProgress,
            "A subscription request for this plan is already in progress; retry shortly.");
    }

    private async Task CompleteSubscriptionAsync(MaxioSubscriptionLink link, int? subscriptionId)
    {
        link.MaxioSubscriptionId = subscriptionId;
        link.Status = MaxioLinkStatus.Active;
        if (_db.Entry(link).State == EntityState.Detached) _db.Attach(link).State = EntityState.Modified;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task MarkSubscriptionActiveAsync(string userId, string planHandle, int? subscriptionId)
    {
        var row = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserId == userId && l.PlanHandle == planHandle);
        if (row is null) return;
        row.MaxioSubscriptionId = subscriptionId;
        row.Status = MaxioLinkStatus.Active;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(MaxioSubscriptionLink link)
    {
        _db.Entry(link).State = EntityState.Deleted;
        try { await _db.SaveChangesAsync(CancellationToken.None); }
        catch (Exception ex) { _logger.LogError(ex, "Could not release subscription claim"); _db.Entry(link).State = EntityState.Detached; }
    }

    private async Task<(Subscription? Sub, bool Absent)> ReconcileSubscriptionAsync(string reference, int? customerId, string planHandle)
    {
        using var cts = new CancellationTokenSource(ReconcileBudget);
        try
        {
            var r = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: cts.Token);
            if (r.Subscription is not null) return (r.Subscription, false);
        }
        catch (ApiException ex) when (ex is not ResponseDeserializationException && ex.StatusCode == HttpStatusCode.NotFound)
        {
            // fall through to the customer-based lookup
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Subscription reconcile read failed: {Reason}", ex.GetType().Name);
            return (null, false);
        }

        if (customerId is null) return (null, true);
        try
        {
            var subs = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value }, cancellationToken: cts.Token);
            var match = subs.Select(s => s.Subscription).FirstOrDefault(s =>
                s?.Product?.Handle == planHandle && s.Reference == reference);
            return (match, match is null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Subscription reconcile list failed: {Reason}", ex.GetType().Name);
            return (null, false);
        }
    }

    // -------------------------------------------------------- my subscriptions

    public async Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        var link = await _db.MaxioCustomerLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);
        if (link?.MaxioCustomerId is null || link.Status != MaxioLinkStatus.Active)
            return Array.Empty<SubscriptionInfo>();

        using var cts = Budget(cancellationToken);
        try
        {
            var plans = await ReadPlansAsync(cts.Token);
            var offered = plans.Plans.ToDictionary(p => p.Handle);
            var subs = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = link.MaxioCustomerId.Value }, cancellationToken: cts.Token);

            return subs
                .Select(s => s.Subscription)
                .Where(s => s?.Product?.Handle is not null && offered.ContainsKey(s.Product.Handle))
                .Select(s => ToInfo(s!, offered[s!.Product!.Handle!]))
                .ToList();
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            throw Translate(ex, "list subscriptions");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static SubscriptionInfo ToInfo(Subscription s, SubscriptionPlanInfo plan) => new(
        s.Id,
        s.Product?.Handle ?? plan.Handle,
        s.Product?.Name ?? plan.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents ?? plan.PriceInCents,
        s.State?.Value,
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);

    private CancellationTokenSource Budget(CancellationToken caller)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(caller);
        cts.CancelAfter(_settings.TotalTimeout);
        return cts;
    }

    private MaxioAdvancedBilling.Models.Enums.CollectionMethod? CollectionMethodFromSettings() =>
        MaxioAdvancedBilling.Models.Enums.CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod, out var m) ? m : null;

    private static string SubscriptionReference(string userId, string planHandle) => $"{userId}:{planHandle}";

    private static string FirstNameFrom(string email)
    {
        var local = (email ?? string.Empty).Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? "Customer" : local;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..12];

    private static bool IsDuplicateKey(Exception ex) =>
        ex is DbUpdateException or InvalidOperationException or ArgumentException;

    /// <summary>The write may have landed: connection/timeout, a 5xx, or a 2xx whose body could not be read.</summary>
    private static bool IsUnknownOutcome(Exception ex) => ex switch
    {
        SdkConnectionException => true,
        ResponseDeserializationException r => (int)r.StatusCode is >= 200 and < 300 or >= 500,
        ApiException a => (int)a.StatusCode >= 500,
        OperationCanceledException => true, // our own budget expiring (caller cancellation is filtered upstream)
        _ => false
    };

    /// <summary>A 422 on customer creation can mean the reference already exists at Maxio.</summary>
    private static bool IsDuplicateReference(Exception ex) =>
        ex is ApiException a && a.StatusCode == HttpStatusCode.UnprocessableEntity;

    private static bool IsProviderFailure(Exception ex, CancellationToken callerToken) =>
        ex is not MaxioBillingException &&
        (ex is SdkException || (ex is OperationCanceledException && !callerToken.IsCancellationRequested));

    /// <summary>Provider error text for our own logs only (never echoed to callers).</summary>
    private static string ProviderDetail(Exception ex)
    {
        string text = ex switch
        {
            ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> e => e.Error.Match(
                onErrorListResponse1: l => string.Join("; ", l.Errors),
                otherwise: raw => raw.ReadAsString()),
            ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> e => e.Error.ReadAsString(),
            _ => string.Empty
        };
        return text.Length > 500 ? text[..500] : text;
    }

    private MaxioBillingException Translate(Exception ex, string operation)
    {
        switch (ex)
        {
            case ApiException a:
                var status = (int)a.StatusCode;
                _logger.LogWarning("Maxio {Operation} answered HTTP {Status}: {Detail}", operation, status, ProviderDetail(ex));
                if (status is 401 or 403 or 429 or >= 500)
                    return new MaxioBillingException(MaxioBillingFailure.ProviderUnavailable, "Billing provider unavailable.", ex);
                if (ex is ResponseDeserializationException)
                    return new MaxioBillingException(MaxioBillingFailure.ProviderUnavailable, "Billing provider returned an unreadable response.", ex);
                return new MaxioBillingException(MaxioBillingFailure.Rejected, "The billing provider rejected the request.", ex);
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio credentials missing or rejected during {Operation}", operation);
                return new MaxioBillingException(MaxioBillingFailure.ProviderUnavailable, "Billing provider unavailable.", ex);
            default:
                _logger.LogWarning("Maxio {Operation} failed: {Reason}", operation, ex.GetType().Name);
                return new MaxioBillingException(MaxioBillingFailure.ProviderUnavailable, "Billing provider unavailable.", ex);
        }
    }
}
