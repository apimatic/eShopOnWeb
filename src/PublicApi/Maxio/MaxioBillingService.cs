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
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingService
{
    Task<PlanListResult> ListPlansAsync(CancellationToken ct);
    Task<SubscribeResult> SubscribeAsync(string? userName, string? planHandle, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string? userName, CancellationToken ct);
}

/// <summary>
/// Subscribe flow against Maxio. Order for the write: claim (EF primary key) -> Maxio calls -> record result.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private const int PageSize = 200;
    private const int MaxPages = 5;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PlanCacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StalePendingWindow = TimeSpan.FromMinutes(5);
    private const string PlanCacheKey = "maxio:plans";

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings,
        CatalogContext db, UserManager<ApplicationUser> users, IMemoryCache cache,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _db = db;
        _users = users;
        _cache = cache;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<PlanListResult> ListPlansAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(PlanCacheKey, out PlanListResult? cached) && cached is not null)
            return cached;

        using var budget = Budget(ct, ReadBudget);
        var result = await RunAsync(() => ReadPlansAsync(budget.Token), budget, ct);
        // only a complete list is cached; a cut-short list is never served as if it were whole
        if (result.IsComplete)
            _cache.Set(PlanCacheKey, result, PlanCacheTtl);
        return result;
    }

    // Reads every page (PerPage 200) until an empty/short page, or the page cap.
    private async Task<PlanListResult> ReadPlansAsync(CancellationToken token)
    {
        var plans = new List<SubscriptionPlanDto>();
        var complete = false;
        for (var page = 1; page <= MaxPages; page++)
        {
            var items = await _client.ProductFamilies.ListProductsForProductFamily(
                new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                    Page = page,
                    PerPage = PageSize
                },
                cancellationToken: token);

            foreach (var item in items)
            {
                var p = item.Product;
                if (p?.Handle is null || p.ArchivedAt is not null)
                    continue;
                plans.Add(new SubscriptionPlanDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Price = p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : null,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit?.Value,
                    RequiresPaymentMethod = p.RequireCreditCard
                });
            }

            if (items.Count < PageSize)
            {
                complete = true;
                break;
            }
        }
        return new PlanListResult(plans, complete);
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(string? userName, string? planHandle, CancellationToken ct)
    {
        var user = await ResolveUserAsync(userName);
        planHandle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle;
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(400, "planHandle is required.");

        using var budget = Budget(ct, SubscribeBudget);

        // Invariant: the handle must be one this application offers (the family's plan list), read fresh.
        var offered = await RunAsync(() => ReadPlansAsync(budget.Token), budget, ct);
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (!offered.IsComplete)
                throw new BillingException(503, "The plan list could not be fully read; try again shortly.");
            throw new BillingException(400, $"Unknown plan '{planHandle}'.");
        }
        if (plan.RequiresPaymentMethod == true)
            throw new BillingException(422, "This plan requires a payment method, which is not supported by this endpoint.");

        var reference = SubscriptionReferenceFor(user.Id);

        // CLAIM: the primary key on UserId refuses the second caller before anything reaches Maxio.
        var claim = new SubscriptionClaim(user.Id, plan.Handle, reference, DateTime.UtcNow);
        _db.SubscriptionClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            // the store refused the second claim for this user (the in-memory provider raises
            // InvalidOperationException/ArgumentException for a duplicate key, relational ones DbUpdateException)
            _db.Entry(claim).State = EntityState.Detached;
            _logger.LogInformation("Subscribe claim lost for user {UserId}; answering from the existing claim", user.Id);
            return await AnswerForLostClaimAsync(user.Id, reference, plan, ct);
        }

        _logger.LogInformation("Subscribe claim won for user {UserId}, plan {Plan}", user.Id, plan.Handle);

        try
        {
            var customerId = await EnsureCustomerAsync(user, budget, ct);
            claim.SetCustomer(customerId);
            await _db.SaveChangesAsync(CancellationToken.None);

            Subscription? sub;
            try
            {
                var response = await RunAsync(() => _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = plan.Handle,
                                CustomerId = customerId,
                                Reference = reference,
                                PaymentCollectionMethod = CollectionMethodFor(_settings.PaymentCollectionMethod)
                            }
                        }
                    }, cancellationToken: budget.Token), budget, ct, writeOperation: true,
                    typedRefusal: ex => ex is ApiException<CreateSubscriptionError> typed
                                        && typed.StatusCode == HttpStatusCode.UnprocessableEntity
                        ? DescribeSubscriptionRefusal(typed)
                        : null);
                sub = response.Subscription;
            }
            catch (BillingException ex) when (ex.OutcomeUnknown)
            {
                // the write may have landed: settle by re-reading with the reference we sent
                sub = await SettleSubscriptionAsync(reference)
                    ?? throw new BillingException(503,
                        "The subscription outcome is not yet known; retry shortly to confirm it.", inner: ex);
            }

            if (sub is null)
                throw new BillingException(502, "Billing provider returned an empty subscription.", outcomeUnknown: true);

            claim.Complete(sub.Id);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Subscription {SubscriptionId} created for user {UserId}, plan {Plan}, state {State}",
                sub.Id, user.Id, plan.Handle, sub.State?.Value);
            return new SubscribeResult(Map(sub, plan), false);
        }
        catch (BillingException ex) when (ex.Refused)
        {
            // provider refused definitively: release the claim so the user can retry once the cause is fixed
            _logger.LogWarning("Maxio refused the subscribe for user {UserId}: {Message}", user.Id, ex.Message);
            _db.SubscriptionClaims.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<SubscribeResult> AnswerForLostClaimAsync(string userId, string reference,
        SubscriptionPlanDto requestedPlan, CancellationToken ct)
    {
        var existing = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (existing is null)
            throw new BillingException(409, "A concurrent request just finished; retry.");

        if (existing.Status == SubscriptionClaimStatus.Completed && existing.MaxioSubscriptionId is int subId)
        {
            using var budget = Budget(ct, ReadBudget);
            var response = await RunAsync(() => _client.Subscriptions.ReadSubscription(
                new ReadSubscriptionRequest { SubscriptionId = subId }, cancellationToken: budget.Token), budget, ct);
            if (response.Subscription is null)
                throw new BillingException(502, "Billing provider returned an empty subscription.");
            return new SubscribeResult(Map(response.Subscription, PlanStub(existing.PlanHandle, requestedPlan)), true);
        }

        // Pending: an earlier attempt is in flight or died. Settle by reference before deciding anything.
        var found = await SettleSubscriptionAsync(existing.SubscriptionReference);
        if (found is not null)
        {
            existing.Complete(found.Id);
            await _db.SaveChangesAsync(CancellationToken.None);
            return new SubscribeResult(Map(found, PlanStub(existing.PlanHandle, requestedPlan)), true);
        }

        if (DateTime.UtcNow - existing.CreatedAtUtc > StalePendingWindow)
        {
            _db.SubscriptionClaims.Remove(existing);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new BillingException(409, "A previous attempt expired without creating a subscription; please retry.");
        }
        throw new BillingException(409, "A subscribe request for this account is already in progress.");
    }

    private static SubscriptionPlanDto PlanStub(string handle, SubscriptionPlanDto fallback) =>
        string.Equals(handle, fallback.Handle, StringComparison.Ordinal) ? fallback : new SubscriptionPlanDto { Handle = handle };

    // Re-reads by subscription reference with its own short budget (the call budget may be spent).
    private async Task<Subscription?> SettleSubscriptionAsync(string reference)
    {
        using var settle = new CancellationTokenSource(SettleBudget);
        try
        {
            var response = await RunAsync(() => FindSubscriptionOrNullAsync(reference, settle.Token), settle, CancellationToken.None);
            return response?.Subscription;
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex, "Could not settle subscription outcome for reference {Reference}", reference);
            return null;
        }
    }

    private async Task<SubscriptionResponse?> FindSubscriptionOrNullAsync(string reference, CancellationToken token)
    {
        try
        {
            return await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference }, cancellationToken: token);
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(ApplicationUser user, CancellationTokenSource budget, CancellationToken ct)
    {
        var existing = await RunAsync(() => FindCustomerOrNullAsync(user.Id, budget.Token), budget, ct);
        if (existing?.Customer?.Id is int existingId)
            return existingId;

        try
        {
            var email = user.Email ?? user.UserName ?? user.Id;
            var local = email.Split('@')[0];
            var created = await RunAsync(() => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = local,
                            LastName = "Customer",
                            Email = email,
                            Reference = user.Id
                        }
                    }
                }, cancellationToken: budget.Token), budget, ct, writeOperation: true);
            if (created.Customer?.Id is not int createdId)
                throw new BillingException(502, "Billing provider returned a customer without an id.", outcomeUnknown: true);
            _logger.LogInformation("Maxio customer {CustomerId} created for user {UserId}", createdId, user.Id);
            return createdId;
        }
        catch (BillingException ex) when (ex.OutcomeUnknown)
        {
            using var settle = new CancellationTokenSource(SettleBudget);
            CustomerResponse? found = null;
            try { found = await RunAsync(() => FindCustomerOrNullAsync(user.Id, settle.Token), settle, CancellationToken.None); }
            catch (BillingException) { /* still unknown */ }
            if (found?.Customer?.Id is int foundId)
                return foundId;
            throw new BillingException(503, "The customer outcome is not yet known; retry shortly.", inner: ex);
        }
    }

    // ReadCustomerByReference is Case B: a missing customer is a raw 404. A deserialization failure is NOT absence.
    private async Task<CustomerResponse?> FindCustomerOrNullAsync(string reference, CancellationToken token)
    {
        try
        {
            return await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: token);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static BillingException DescribeSubscriptionRefusal(ApiException<CreateSubscriptionError> ex)
    {
        var detail = ex.Error.TryGetErrorListResponse1(out var list) && list.Errors is { Count: > 0 }
            ? string.Join("; ", list.Errors)
            : "rejected by the billing provider";
        return new BillingException(422, $"Subscription refused: {detail}", refused: true);
    }

    // ---------------------------------------------------------- my-subscriptions

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string? userName, CancellationToken ct)
    {
        var user = await ResolveUserAsync(userName);
        // the customer id comes only from this user's own claim row, never from the request
        var claim = await _db.SubscriptionClaims.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == user.Id, ct);
        if (claim?.MaxioCustomerId is not int customerId)
            return Array.Empty<SubscriptionDto>();

        using var budget = Budget(ct, ReadBudget);
        var items = await RunAsync(() => _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: budget.Token), budget, ct);

        return items.Where(i => i.Subscription is not null)
            .Select(i => Map(i.Subscription!, new SubscriptionPlanDto
            {
                Handle = claim.MaxioSubscriptionId == i.Subscription!.Id ? claim.PlanHandle : string.Empty
            }))
            .ToList();
    }

    // -------------------------------------------------------------- helpers

    // Default remittance: invoice-based collection, so signup needs no card (plans here require none).
    private static MaxioAdvancedBilling.Models.Enums.CollectionMethod CollectionMethodFor(string? configured) =>
        configured?.Trim().ToLowerInvariant() switch
        {
            "automatic" => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Automatic,
            "prepaid" => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Prepaid,
            _ => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
        };

    private static string SubscriptionReferenceFor(string userId) => "eshop-sub-" + userId;

    private async Task<ApplicationUser> ResolveUserAsync(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new BillingException(401, "Unauthenticated.");
        var user = await _users.FindByNameAsync(userName);
        return user ?? throw new BillingException(401, "Unknown user.");
    }

    private static SubscriptionDto Map(Subscription s, SubscriptionPlanDto plan)
    {
        var cents = s.ProductPriceInCents ?? plan.PriceInCents;
        return new SubscriptionDto
        {
            Id = s.Id,
            Reference = s.Reference,
            PlanHandle = s.Product?.Handle ?? (string.IsNullOrEmpty(plan.Handle) ? null : plan.Handle),
            PlanName = s.Product?.Name ?? plan.Name,
            PriceInCents = cents,
            Price = cents.HasValue ? cents.Value / 100m : null,
            State = s.State?.Value,
            NextBillingAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
        };
    }

    // One whole-call budget, linked to the caller's own cancellation.
    private static CancellationTokenSource Budget(CancellationToken ct, TimeSpan budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        return cts;
    }

    /// <summary>
    /// The single place SDK failures become <see cref="BillingException"/>. Same ladder for every call.
    /// </summary>
    private async Task<T> RunAsync<T>(Func<Task<T>> call, CancellationTokenSource budget, CancellationToken caller,
        bool writeOperation = false, Func<Exception, BillingException?>? typedRefusal = null)
    {
        try
        {
            return await call();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !caller.IsCancellationRequested)
        {
            throw new BillingException(504, "The billing provider did not answer in time.", outcomeUnknown: writeOperation);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError(ex, "Maxio response could not be processed (status {Status})", (int)ex.StatusCode);
            var success = (int)ex.StatusCode is >= 200 and < 300;
            throw new BillingException(502, "The billing provider returned a response that could not be processed.",
                outcomeUnknown: writeOperation && success, inner: ex);
        }
        catch (ApiException ex)
        {
            var typed = typedRefusal?.Invoke(ex);
            if (typed is not null)
                throw typed;
            var status = (int)ex.StatusCode;
            var body = ex is ApiException<RawError> raw ? Truncate(raw.Error.ReadAsString()) : null;
            _logger.LogWarning("Maxio {Method} {Uri} returned {Status} {Body}", ex.Method, ex.RequestUri, status, body);
            // 401/403 = our credentials, 429 = our quota: not the caller's problem
            if (status is 429)
                throw new BillingException(503, "The billing provider is busy; try again shortly.", inner: ex);
            var refused = status is >= 400 and < 500 && status is not (401 or 403 or 408 or 429);
            throw new BillingException(502, "The billing provider rejected or failed the request.",
                refused: refused && writeOperation, outcomeUnknown: writeOperation && status >= 500, inner: ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new BillingException(504, "The billing provider did not answer in time.", outcomeUnknown: writeOperation, inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new BillingException(503, "The billing provider is unreachable.", outcomeUnknown: writeOperation, inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials are missing or could not be applied");
            throw new BillingException(502, "The billing provider is unavailable.", inner: ex);
        }
    }

    private static string? Truncate(string? s) => s is { Length: > 300 } ? s[..300] : s;
}
