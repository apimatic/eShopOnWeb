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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Subscribe flow against Maxio. Order for every write: claim (local store) -> SDK call -> record result.
/// </summary>
public class MaxioSubscriptionService
{
    private const int PER_PAGE = 200;
    private const int MAX_PAGES = 10;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan StalePendingAfter = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioOptions options, CatalogContext db,
        UserManager<ApplicationUser> users, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _db = db;
        _users = users;
        _logger = logger;
    }

    // ---------- plans ----------

    public Task<PlanListing> ListPlansAsync(CancellationToken ct) => Bounded(ListPlansCoreAsync, ct);

    private async Task<PlanListing> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<PlanDto>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            var pageNo = page;
            IReadOnlyList<ProductResponse> items = await Guard("list plans", () =>
                _client.ProductFamilies.ListProductsForProductFamily(new ListProductsForProductFamilyRequest
                {
                    ProductFamilyId = "handle:" + _options.ProductFamilyHandle,
                    Page = pageNo,
                    PerPage = PER_PAGE
                }, cancellationToken: ct));

            foreach (var item in items)
            {
                var p = item.Product;
                // Plans that demand card capture cannot be subscribed to here (no payment profile is sent).
                if (string.IsNullOrEmpty(p.Handle) || p.ArchivedAt != null || p.RequireCreditCard == true) continue;
                plans.Add(new PlanDto
                {
                    Handle = p.Handle!,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Price = p.PriceInCents / 100m,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit?.Value
                });
            }

            if (items.Count < PER_PAGE) break;
            if (page >= MAX_PAGES) { truncated = true; break; }
        }
        return new PlanListing { Plans = plans, Truncated = truncated };
    }

    // ---------- subscribe ----------

    public Task<SubscribeOutcome> SubscribeAsync(string userName, string planHandle, CancellationToken ct) =>
        Bounded(token => SubscribeCoreAsync(userName, planHandle, token), ct);

    private async Task<SubscribeOutcome> SubscribeCoreAsync(string userName, string planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new BillingException(BillingFailure.InvalidRequest, "planHandle is required.");

        var user = await ResolveUser(userName);

        // Invariant: only a plan the plans listing offers may be subscribed to.
        var listing = await ListPlansCoreAsync(ct);
        var plan = listing.Plans.FirstOrDefault(p => p.Handle == planHandle);
        if (plan == null)
        {
            if (listing.Truncated)
                throw new BillingException(BillingFailure.Unavailable, "The plan catalog could not be read completely; try again later.");
            throw new BillingException(BillingFailure.InvalidRequest, $"Plan '{planHandle}' is not available.");
        }

        var customerId = await EnsureCustomerAsync(user, ct);
        return await EnsureSubscriptionAsync(user.Id, customerId, plan, ct);
    }

    private async Task<int> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct)
    {
        var link = new MaxioCustomerLink { UserId = user.Id };
        if (!await TryClaim(link, ct))
        {
            var existing = await _db.MaxioCustomerLinks.AsNoTracking().FirstAsync(l => l.UserId == user.Id, ct);
            if (existing.MaxioCustomerId is int known) return known;
            // Pending claim: another request is creating it, or an earlier one died. Ask the provider.
            var found = await FindCustomerAsync(user.Id, ct);
            if (found == null)
                throw new BillingException(BillingFailure.Conflict, "Your customer profile is being created; retry shortly.");
            await CompleteCustomerLink(user.Id, found.Value, ct);
            return found.Value;
        }

        try
        {
            // The provider may already know this user (local store reset/restart): reuse, never recreate.
            var id = await FindCustomerAsync(user.Id, ct) ?? await CreateCustomerAsync(user, ct);
            await CompleteCustomerLink(user.Id, id, ct);
            return id;
        }
        catch (BillingException ex) when (ex.Failure is BillingFailure.InvalidRequest or BillingFailure.Unavailable)
        {
            await Release(link);   // provider refused / never acted: free the claim so the user can retry
            throw;
        }
        // BillingFailure.Unknown: the claim stays pending; the next call reconciles by reference.
    }

    private async Task<int> CreateCustomerAsync(ApplicationUser user, CancellationToken ct)
    {
        var email = user.Email ?? user.UserName ?? user.Id;
        var local = email.Split('@')[0];
        try
        {
            var res = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local,
                        LastName = "Customer",
                        Email = email,
                        Reference = user.Id
                    }
                }
            }, cancellationToken: ct);
            return res.Customer.Id ?? throw new BillingException(BillingFailure.Unknown, "Billing provider returned an incomplete customer.");
        }
        catch (ApiException<CreateCustomerError> ex) when (IsRejection(ex))
        {
            Log4xx("create customer", ex.StatusCode, ex.Message);
            throw new BillingException(MapClientStatus(ex.StatusCode), "The billing provider rejected the customer details.", ex);
        }
        catch (SdkException ex) when (ex is ApiException a && IsRejection(a))
        {
            throw new BillingException(MapClientStatus(a.StatusCode), "The billing provider rejected the customer details.", ex);
        }
        catch (SdkException ex)
        {
            // Unknown outcome (5xx, timeout, connection, unreadable 2xx): settle by re-reading with the reference.
            _logger.LogWarning(ex, "CreateCustomer outcome unknown; reconciling by reference for user {UserId}", user.Id);
            return await Settle(() => FindCustomerAsync(user.Id, ct), ex);
        }
    }

    private async Task<int?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var res = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return res.Customer.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate("look up customer", ex);
        }
    }

    private async Task<SubscribeOutcome> EnsureSubscriptionAsync(string userId, int customerId, PlanDto plan, CancellationToken ct)
    {
        var claim = new MaxioSubscriptionClaim
        {
            UserId = userId,
            ProductHandle = plan.Handle,
            Reference = $"eshop:{userId}:{plan.Handle}"
        };

        if (!await TryClaim(claim, ct))
        {
            var existing = await _db.MaxioSubscriptionClaims.AsNoTracking()
                .FirstAsync(c => c.UserId == userId && c.ProductHandle == plan.Handle, ct);
            var sub = await FindSubscriptionByReferenceAsync(existing.Reference, ct);
            if (sub != null)
            {
                if (existing.Status != MaxioSubscriptionClaimStatus.Completed)
                    await CompleteClaim(userId, plan.Handle, sub.Id, ct);
                return new SubscribeOutcome(ToDto(sub, plan), false);   // idempotent repeat
            }
            if (existing.Status == MaxioSubscriptionClaimStatus.Pending
                && DateTimeOffset.UtcNow - existing.CreatedAt < StalePendingAfter)
                throw new BillingException(BillingFailure.Conflict, "This subscription is already being created; retry shortly.");

            // Stale claim and the provider holds nothing under the reference: take it over once.
            await Release(existing);
            if (!await TryClaim(claim, ct))
                throw new BillingException(BillingFailure.Conflict, "This subscription is already being created; retry shortly.");
        }

        try
        {
            // The provider may already hold it (store reset/restart): reuse, never duplicate.
            var already = await FindSubscriptionByReferenceAsync(claim.Reference, ct);
            var created = already != null ? null : await CreateSubscriptionAsync(claim, customerId, ct);
            var sub = already ?? created!;
            await CompleteClaim(userId, plan.Handle, sub.Id, ct);
            _logger.LogInformation("Subscribed user {UserId} to {Plan}: subscription {SubscriptionId} state {State}",
                userId, plan.Handle, sub.Id, sub.State?.Value);
            return new SubscribeOutcome(ToDto(sub, plan), already == null);
        }
        catch (BillingException ex) when (ex.Failure is BillingFailure.InvalidRequest or BillingFailure.Unavailable)
        {
            await Release(claim);
            throw;
        }
        // BillingFailure.Unknown: the claim stays pending; the next subscribe reconciles by reference.
    }

    private async Task<Subscription> CreateSubscriptionAsync(MaxioSubscriptionClaim claim, int customerId, CancellationToken ct)
    {
        try
        {
            var res = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = claim.ProductHandle,
                        CustomerId = customerId,
                        Reference = claim.Reference,
                        // Pay-by-invoice: no card capture, so a plan with an up-front charge can still be subscribed to.
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                    }
                }
            }, cancellationToken: ct);
            return res.Subscription ?? throw new BillingException(BillingFailure.Unknown, "Billing provider returned an incomplete subscription.");
        }
        catch (ApiException<CreateSubscriptionError> ex) when (IsRejection(ex))
        {
            var detail = ex.Error.TryGetErrorListResponse1(out var list) ? string.Join("; ", list.Errors) : null;
            Log4xx("create subscription", ex.StatusCode, detail ?? ex.Message);
            throw new BillingException(MapClientStatus(ex.StatusCode),
                detail is null ? "The billing provider rejected the subscription." : $"The billing provider rejected the subscription: {detail}", ex);
        }
        catch (SdkException ex) when (ex is ApiException a && IsRejection(a))
        {
            throw new BillingException(MapClientStatus(((ApiException)ex).StatusCode), "The billing provider rejected the subscription.", ex);
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "CreateSubscription outcome unknown; reconciling by reference {Reference}", claim.Reference);
            return await Settle(() => FindSubscriptionByReferenceAsync(claim.Reference, ct), ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var res = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return res.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate("find subscription", ex);
        }
    }

    // ---------- my subscriptions ----------

    public Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken ct) =>
        Bounded(token => ListMineCoreAsync(userName, token), ct);

    private async Task<IReadOnlyList<SubscriptionDto>> ListMineCoreAsync(string userName, CancellationToken ct)
    {
        var user = await ResolveUser(userName);
        // Starts at the provider, by the user's own reference — never from the local row alone.
        var customerId = await FindCustomerAsync(user.Id, ct);
        if (customerId == null) return Array.Empty<SubscriptionDto>();

        IReadOnlyList<SubscriptionResponse> subs = await Guard("list subscriptions", () =>
            _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value }, cancellationToken: ct));
        return subs.Where(s => s.Subscription != null).Select(s => ToDto(s.Subscription!, null)).ToList();
    }

    // ---------- helpers ----------

    private async Task<ApplicationUser> ResolveUser(string userName)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? null : await _users.FindByNameAsync(userName);
        return user ?? throw new BillingException(BillingFailure.NotFound, "User not found.");
    }

    /// <summary>Inserts the claim row; false when the store refuses it because the key already exists.</summary>
    private async Task<bool> TryClaim<T>(T claim, CancellationToken ct) where T : class
    {
        var entry = _db.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);
            entry.State = EntityState.Detached;
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            entry.State = EntityState.Detached;
            // Only a real duplicate counts; any other store fault propagates.
            if (await ExistsAsync(claim, ct)) return false;
            throw;
        }
    }

    private Task<bool> ExistsAsync<T>(T claim, CancellationToken ct) where T : class => claim switch
    {
        MaxioCustomerLink l => _db.MaxioCustomerLinks.AsNoTracking().AnyAsync(x => x.UserId == l.UserId, ct),
        MaxioSubscriptionClaim c => _db.MaxioSubscriptionClaims.AsNoTracking().AnyAsync(x => x.UserId == c.UserId && x.ProductHandle == c.ProductHandle, ct),
        _ => Task.FromResult(false)
    };

    private async Task Release<T>(T claim) where T : class
    {
        try
        {
            _db.ChangeTracker.Clear();
            _db.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not release a billing claim");
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    private async Task CompleteCustomerLink(string userId, int customerId, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.MaxioCustomerLinks.FirstAsync(l => l.UserId == userId, ct);
        row.MaxioCustomerId = customerId;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task CompleteClaim(string userId, string handle, int? subscriptionId, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.MaxioSubscriptionClaims.FirstAsync(c => c.UserId == userId && c.ProductHandle == handle, ct);
        row.MaxioSubscriptionId = subscriptionId;
        row.Status = MaxioSubscriptionClaimStatus.Completed;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Settles a write whose outcome is unknown by re-reading provider state: found -> success;
    /// confirmed absent -> nothing happened (Unavailable, claim released); re-read failed too -> Unknown (claim stays).
    /// </summary>
    private static async Task<T> Settle<T>(Func<Task<T?>> reread, Exception cause) where T : struct
    {
        T? found;
        try { found = await reread(); }
        catch (BillingException)
        {
            throw new BillingException(BillingFailure.Unknown, "The billing provider did not confirm the request; retry shortly.", cause);
        }
        return found ?? throw new BillingException(BillingFailure.Unavailable, "Billing is temporarily unavailable; nothing was created.", cause);
    }

    private static async Task<T> Settle<T>(Func<Task<T?>> reread, Exception cause) where T : class
    {
        T? found;
        try { found = await reread(); }
        catch (BillingException)
        {
            throw new BillingException(BillingFailure.Unknown, "The billing provider did not confirm the request; retry shortly.", cause);
        }
        return found ?? throw new BillingException(BillingFailure.Unavailable, "Billing is temporarily unavailable; nothing was created.", cause);
    }

    private static bool IsRejection(ApiException ex) => (int)ex.StatusCode is >= 400 and < 500;

    private static SubscriptionDto ToDto(Subscription s, PlanDto? plan)
    {
        var cents = s.ProductPriceInCents ?? plan?.PriceInCents;
        return new SubscriptionDto
        {
            Id = s.Id,
            PlanHandle = s.Product?.Handle ?? plan?.Handle,
            PlanName = s.Product?.Name ?? plan?.Name,
            PriceInCents = cents,
            Price = cents / 100m,
            Interval = s.Product?.Interval ?? plan?.Interval,
            IntervalUnit = s.Product?.IntervalUnit?.Value ?? plan?.IntervalUnit,
            State = s.State?.Value,
            NextBillingDate = s.NextAssessmentAt
        };
    }

    private static BillingFailure MapClientStatus(HttpStatusCode status) => (int)status switch
    {
        401 or 403 or 429 => BillingFailure.Unavailable,   // our credentials / our quota, not the caller's fault
        _ => BillingFailure.InvalidRequest
    };

    private void Log4xx(string op, HttpStatusCode status, string? detail) =>
        _logger.LogWarning("Maxio {Operation} rejected with HTTP {Status}: {Detail}", op, (int)status, detail);

    /// <summary>Runs a read-only SDK call and converts every SDK failure to a BillingException.</summary>
    private async Task<T> Guard<T>(string op, Func<Task<T>> call)
    {
        try { return await call(); }
        catch (SdkException ex) { throw Translate(op, ex); }
    }

    private BillingException Translate(string op, SdkException ex)
    {
        if (ex is ApiException a)
        {
            _logger.LogError(ex, "Maxio {Operation} failed with HTTP {Status}", op, (int)a.StatusCode);
            return new BillingException(BillingFailure.Unavailable, "The billing provider could not process the request.", ex);
        }
        _logger.LogError(ex, "Maxio {Operation} failed without a usable response", op);
        return new BillingException(BillingFailure.Unavailable, "The billing provider is unavailable.", ex);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        try { return await call(cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(BillingFailure.Unknown, "The request took too long; retry shortly.");
        }
    }
}
