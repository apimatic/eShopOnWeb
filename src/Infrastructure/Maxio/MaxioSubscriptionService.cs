using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioModels = MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing (system of record).
/// Every write follows: claim (row in our own store) -> reconcile with Maxio -> SDK write -> record result.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PerPage = 200;
    private const int MaxPages = 10;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SubscribeBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleBudget = TimeSpan.FromSeconds(10);
    /// <summary>A Pending claim older than this is considered abandoned (e.g. the process died) and can be taken over.</summary>
    private static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly TimeProvider _time;

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
        _time = TimeProvider.System;
    }

    // ------------------------------------------------------------------ plans

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        using var cts = Budget(ReadBudget, cancellationToken);
        try
        {
            return await ReadPlansAsync(cts.Token);
        }
        catch (Exception ex) when (ex is not SubscriptionException && !cancellationToken.IsCancellationRequested)
        {
            throw TranslateReadFailure(ex, "list plans");
        }
    }

    private async Task<SubscriptionPlanList> ReadPlansAsync(CancellationToken ct)
    {
        var plans = new List<SubscriptionPlan>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            var products = await _client.ProductFamilies.ListProductsForProductFamily(
                new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                    Page = page,
                    PerPage = PerPage,
                },
                cancellationToken: ct);

            foreach (var wrapper in products)
            {
                var p = wrapper.Product;
                if (p.ArchivedAt is not null || string.IsNullOrWhiteSpace(p.Handle) || p.PriceInCents is null)
                    continue;
                plans.Add(new SubscriptionPlan(p.Handle!, p.Name ?? p.Handle!, p.Description, p.PriceInCents.Value,
                    p.Interval, p.IntervalUnit?.Value));
            }

            if (products.Count < PerPage) break;
            if (page >= MaxPages) { truncated = true; break; }
        }

        _logger.LogInformation("Listed {Count} subscription plans (truncated: {Truncated})", plans.Count, truncated);
        return new SubscriptionPlanList(plans, truncated);
    }

    // -------------------------------------------------------------- subscribe

    public async Task<SubscribeResult> SubscribeAsync(string userId, string userName, string? email, string planHandle, CancellationToken cancellationToken = default)
    {
        using var cts = Budget(SubscribeBudget, cancellationToken);
        var ct = cts.Token;

        // Invariant: the plan must be one the configured product family currently offers.
        SubscriptionPlanList offered;
        try
        {
            offered = await ReadPlansAsync(ct);
        }
        catch (Exception ex) when (ex is not SubscriptionException && !cancellationToken.IsCancellationRequested)
        {
            throw TranslateReadFailure(ex, "validate plan");
        }

        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            if (offered.IsTruncated)
                throw new SubscriptionException(SubscriptionFailure.ProviderUnavailable, "The plan catalog is too large to validate the requested plan right now.");
            _logger.LogWarning("Subscribe rejected: plan {PlanHandle} is not offered", planHandle);
            throw new SubscriptionException(SubscriptionFailure.PlanNotOffered, $"Plan '{planHandle}' is not available.");
        }

        var customerId = await EnsureCustomerAsync(userId, userName, email, cancellationToken, ct);
        return await EnsureSubscriptionAsync(userId, plan, customerId, cancellationToken, ct);
    }

    private static string CustomerReference(string userId) => userId;
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop:{userId}:{planHandle}";

    // ---- customer

    private async Task<int> EnsureCustomerAsync(string userId, string userName, string? email, CancellationToken callerCt, CancellationToken ct)
    {
        var claim = await ClaimCustomerAsync(userId, ct);
        if (claim.Existing is int existingId)
            return existingId;
        var link = claim.Owned!;

        try
        {
            // Reconcile first: a customer with our reference may already exist (earlier attempt, lost local store).
            var found = await FindCustomerAsync(userId, ct);
            if (found is not null)
            {
                await CompleteCustomerAsync(link, found.Value);
                _logger.LogInformation("Maxio customer {CustomerId} found for user {UserId}", found, userId);
                return found.Value;
            }

            var created = await _client.Customers.CreateCustomer(
                new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                {
                    Body = new MaxioModels.CreateCustomerRequest
                    {
                        Customer = new MaxioModels.CreateCustomer
                        {
                            FirstName = FirstNameFor(userName, email),
                            LastName = "Shopper",
                            Email = string.IsNullOrWhiteSpace(email) ? $"{userId}@users.invalid" : email!,
                            Reference = CustomerReference(userId),
                        },
                    },
                },
                cancellationToken: ct);

            var id = created.Customer.Id ?? throw new InvalidOperationException("Billing provider returned a customer without an id.");
            await CompleteCustomerAsync(link, id);
            _logger.LogInformation("Maxio customer {CustomerId} created for user {UserId}", id, userId);
            return id;
        }
        catch (Exception ex) when (IsDefiniteRejection(ex))
        {
            await ReleaseAsync(link);
            throw TranslateWriteRejection(ex, "create customer");
        }
        catch (Exception ex) when (!callerCt.IsCancellationRequested && ex is not SubscriptionException)
        {
            // The connection failed after the provider may have acted: settle by re-reading by our reference.
            _logger.LogWarning(ex, "CreateCustomer outcome unknown for user {UserId}; re-reading", userId);
            var settled = await TrySettleAsync(token => FindCustomerAsync(userId, token));
            if (settled is not null)
            {
                await CompleteCustomerAsync(link, settled.Value);
                return settled.Value;
            }
            await MarkUnknownAsync(link);
            throw new SubscriptionException(SubscriptionFailure.OutcomeUnknown,
                "Your subscription is being set up with the billing provider; please retry shortly.", ex);
        }
    }

    private async Task<int?> FindCustomerAsync(string userId, CancellationToken ct)
    {
        try
        {
            var r = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = CustomerReference(userId) },
                cancellationToken: ct);
            return r.Customer.Id;
        }
        catch (ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<(int? Existing, MaxioCustomerLink? Owned)> ClaimCustomerAsync(string userId, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var link = new MaxioCustomerLink { UserId = userId, Status = ClaimStatus.Pending, ClaimedAt = now };
        _db.MaxioCustomerLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(ct);
            return (null, link);
        }
        catch (Exception ex) when (IsClaimRefused(ex)) // the primary key refused the second claim
        {
            _db.Entry(link).State = EntityState.Detached;
        }

        var existing = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId, ct);
        if (existing is null) // released between our insert and read; ask the caller to retry
            throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request is already in progress.");
        if (existing.Status == ClaimStatus.Complete && existing.MaxioCustomerId is int id)
            return (id, null);
        if (existing.Status == ClaimStatus.Pending && now - existing.ClaimedAt < StaleClaimAfter)
        {
            _logger.LogWarning("Customer claim for user {UserId} rejected: request in progress", userId);
            throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request is already in progress.");
        }

        // Unknown or abandoned: take it over; the concurrency token lets only one taker win.
        existing.Status = ClaimStatus.Pending;
        existing.ClaimedAt = now;
        existing.ClaimToken = Guid.NewGuid();
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsClaimRefused(ex))
        {
            throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request is already in progress.");
        }
        return (null, existing);
    }

    private async Task CompleteCustomerAsync(MaxioCustomerLink link, int customerId)
    {
        link.MaxioCustomerId = customerId;
        link.Status = ClaimStatus.Complete;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ---- subscription

    private async Task<SubscribeResult> EnsureSubscriptionAsync(string userId, SubscriptionPlan plan, int customerId, CancellationToken callerCt, CancellationToken ct)
    {
        var reference = SubscriptionReference(userId, plan.Handle);
        var claim = await ClaimSubscriptionAsync(userId, plan.Handle, ct);
        if (claim.Existing is not null)
        {
            // Idempotent repeat: report the live subscription.
            var live = (await ListForCustomerAsync(customerId, ct)).FirstOrDefault(s => s.SubscriptionId == claim.Existing);
            _logger.LogInformation("Subscribe repeated for user {UserId}, plan {PlanHandle}; returning existing subscription", userId, plan.Handle);
            return new SubscribeResult(live ?? new SubscriptionInfo(claim.Existing, plan.Handle, plan.Name, plan.PriceInCents, null, null, null), true);
        }
        var row = claim.Owned!;

        try
        {
            // Reconcile first: a previous attempt may have reached Maxio.
            var existing = await FindSubscriptionAsync(customerId, reference, plan.Handle, ct);
            if (existing is not null)
            {
                await CompleteSubscriptionAsync(row, existing.SubscriptionId);
                return new SubscribeResult(existing, true);
            }

            var response = await _client.Subscriptions.CreateSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new MaxioModels.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioModels.CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = reference,
                            // No card is captured: the plans do not require a payment method, so bill by invoice.
                            PaymentCollectionMethod = MaxioModels.Enums.CollectionMethod.Remittance,
                        },
                    },
                },
                cancellationToken: ct);

            var info = ToInfo(response.Subscription ?? throw new InvalidOperationException("Billing provider returned no subscription."));
            await CompleteSubscriptionAsync(row, info.SubscriptionId);
            _logger.LogInformation("Subscription {SubscriptionId} created for user {UserId}, plan {PlanHandle}, state {State}", info.SubscriptionId, userId, plan.Handle, info.State);
            return new SubscribeResult(info with { PlanHandle = info.PlanHandle ?? plan.Handle, PlanName = info.PlanName ?? plan.Name, PriceInCents = info.PriceInCents ?? plan.PriceInCents }, false);
        }
        catch (Exception ex) when (IsDefiniteRejection(ex))
        {
            await ReleaseAsync(row);
            throw TranslateWriteRejection(ex, "create subscription");
        }
        catch (Exception ex) when (!callerCt.IsCancellationRequested && ex is not SubscriptionException)
        {
            _logger.LogWarning(ex, "CreateSubscription outcome unknown for user {UserId}, plan {PlanHandle}; re-reading", userId, plan.Handle);
            var settled = await TrySettleAsync(token => FindSubscriptionAsync(customerId, reference, plan.Handle, token));
            if (settled is not null)
            {
                await CompleteSubscriptionAsync(row, settled.SubscriptionId);
                return new SubscribeResult(settled, false);
            }
            await MarkUnknownAsync(row);
            throw new SubscriptionException(SubscriptionFailure.OutcomeUnknown,
                "Your subscription is being set up with the billing provider; please retry shortly.", ex);
        }
    }

    private async Task<SubscriptionInfo?> FindSubscriptionAsync(int customerId, string reference, string planHandle, CancellationToken ct)
    {
        var all = await ListRawForCustomerAsync(customerId, ct);
        var match = all.FirstOrDefault(s => s.Reference == reference)
            ?? all.FirstOrDefault(s => s.Product?.Handle == planHandle && !IsTerminal(s.State?.Value));
        return match is null ? null : ToInfo(match);
    }

    private static bool IsTerminal(string? state) => state is "canceled" or "expired" or "failed_to_create";

    private async Task<(int? Existing, UserSubscription? Owned)> ClaimSubscriptionAsync(string userId, string planHandle, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var row = new UserSubscription { UserId = userId, PlanHandle = planHandle, Status = ClaimStatus.Pending, ClaimedAt = now };
        _db.UserSubscriptions.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
            return (null, row);
        }
        catch (Exception ex) when (IsClaimRefused(ex)) // composite primary key refused the second claim
        {
            _db.Entry(row).State = EntityState.Detached;
        }

        var existing = await _db.UserSubscriptions.FirstOrDefaultAsync(s => s.UserId == userId && s.PlanHandle == planHandle, ct);
        if (existing is null)
            throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request is already in progress.");
        if (existing.Status == ClaimStatus.Complete && existing.MaxioSubscriptionId is int id)
            return (id, null);
        if (existing.Status == ClaimStatus.Pending && now - existing.ClaimedAt < StaleClaimAfter)
        {
            _logger.LogWarning("Subscription claim for user {UserId}, plan {PlanHandle} rejected: request in progress", userId, planHandle);
            throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request is already in progress.");
        }

        existing.Status = ClaimStatus.Pending;
        existing.ClaimedAt = now;
        existing.ClaimToken = Guid.NewGuid();
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsClaimRefused(ex))
        {
            throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request is already in progress.");
        }
        return (null, existing);
    }

    private async Task CompleteSubscriptionAsync(UserSubscription row, int? subscriptionId)
    {
        row.MaxioSubscriptionId = subscriptionId;
        row.Status = ClaimStatus.Complete;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------- my subscriptions

    public async Task<IReadOnlyList<SubscriptionInfo>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var cts = Budget(ReadBudget, cancellationToken);
        try
        {
            // The Maxio customer id always comes from our own link row, never from the caller.
            var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, cts.Token);
            if (link?.MaxioCustomerId is not int customerId)
                return Array.Empty<SubscriptionInfo>();
            return await ListForCustomerAsync(customerId, cts.Token);
        }
        catch (Exception ex) when (ex is not SubscriptionException && !cancellationToken.IsCancellationRequested)
        {
            throw TranslateReadFailure(ex, "list subscriptions");
        }
    }

    private async Task<IReadOnlyList<SubscriptionInfo>> ListForCustomerAsync(int customerId, CancellationToken ct)
        => (await ListRawForCustomerAsync(customerId, ct)).Select(ToInfo).ToList();

    private async Task<List<MaxioModels.Subscription>> ListRawForCustomerAsync(int customerId, CancellationToken ct)
    {
        var list = await _client.Customers.ListCustomerSubscriptions(
            new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = customerId },
            cancellationToken: ct);
        return list.Select(r => r.Subscription).Where(s => s is not null).Select(s => s!).ToList();
    }

    private static SubscriptionInfo ToInfo(MaxioModels.Subscription s) => new(
        s.Id,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.State?.Value,
        s.CurrentPeriodEndsAt,
        s.NextAssessmentAt);

    // ----------------------------------------------------------------- helpers

    private static CancellationTokenSource Budget(TimeSpan budget, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        return cts;
    }

    private static string FirstNameFor(string userName, string? email)
    {
        var source = !string.IsNullOrWhiteSpace(userName) ? userName : email ?? "Shopper";
        var at = source.IndexOf('@');
        return at > 0 ? source[..at] : source;
    }

    /// <summary>
    /// The store refused a claim: a relational provider raises DbUpdateException; the in-memory provider
    /// raises ArgumentException for a duplicate primary key.
    /// </summary>
    private static bool IsClaimRefused(Exception ex) => ex is DbUpdateException or ArgumentException;

    /// <summary>The provider answered with a non-success status, so the write definitely did not happen.</summary>
    private static bool IsDefiniteRejection(Exception ex) => ex switch
    {
        ResponseDeserializationException d => (int)d.StatusCode >= 400,
        ApiException => true,
        _ => false,
    };

    private SubscriptionException TranslateWriteRejection(Exception ex, string operation)
    {
        var status = ex is ApiException api ? api.StatusCode : (HttpStatusCode?)null;
        _logger.LogWarning("Maxio {Operation} rejected with HTTP {Status}: {Detail}", operation, status is null ? "?" : (int)status.Value, DescribeProviderError(ex));
        // 401/403/429 concern our credentials/quota, not the caller's request.
        return status is { } s && (int)s is >= 400 and < 500 && s is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout)
            ? new SubscriptionException(SubscriptionFailure.Rejected, $"The billing provider rejected the request ({operation}).", ex)
            : new SubscriptionException(SubscriptionFailure.ProviderUnavailable, "The billing provider is temporarily unavailable.", ex);
    }

    private static string DescribeProviderError(Exception ex)
    {
        switch (ex)
        {
            case ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> sub:
                if (sub.Error.TryGetErrorListResponse1(out var list)) return string.Join("; ", list.Errors);
                break;
            case ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> cust:
                if (cust.Error.TryGetCustomerErrorResponse1(out var c)) return c.ToString() ?? string.Empty;
                break;
        }
        return ex.Message;
    }

    private SubscriptionException TranslateReadFailure(Exception ex, string operation)
    {
        if (ex is SdkException sdk)
            _logger.LogError(ex, "Maxio {Operation} failed ({Method} {Uri})", operation, sdk.Method, sdk.RequestUri?.GetLeftPart(UriPartial.Path));
        else
            _logger.LogError(ex, "Maxio {Operation} failed", operation);
        return new SubscriptionException(SubscriptionFailure.ProviderUnavailable, "The billing provider is temporarily unavailable.", ex);
    }

    private async Task<T?> TrySettleAsync<T>(Func<CancellationToken, Task<T?>> read)
    {
        try
        {
            using var cts = new CancellationTokenSource(SettleBudget);
            return await read(cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not settle unknown outcome");
            return default;
        }
    }

    private async Task ReleaseAsync(object claim)
    {
        try
        {
            _db.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not release claim");
        }
    }

    private async Task MarkUnknownAsync(object claim)
    {
        switch (claim)
        {
            case MaxioCustomerLink l: l.Status = ClaimStatus.Unknown; break;
            case UserSubscription s: s.Status = ClaimStatus.Unknown; break;
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }
}
