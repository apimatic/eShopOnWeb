using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing. Every write follows claim -> SDK call -> record:
/// the claim is a row in our own store whose primary key refuses a second concurrent caller.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    internal const string CustomerReferencePrefix = "eshop-user:";
    private const int PlanPageSize = 200;
    private const int MaxPlanPages = 10;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    // A pending claim older than this, whose outcome Maxio cannot confirm, is considered abandoned.
    private static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        CatalogContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger,
        TimeProvider? clock = null)
    {
        _client = client;
        _db = db;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    // ---------------------------------------------------------------- plans

    public Task<SubscriptionPlans> GetPlansAsync(CancellationToken ct) =>
        Bounded(GetPlansCoreAsync, ct);

    private async Task<SubscriptionPlans> GetPlansCoreAsync(CancellationToken ct)
    {
        var products = await GuardRead(async () =>
        {
            var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: ct);
            var family = families
                .Select(f => f.ProductFamily)
                .FirstOrDefault(f => f?.Id != null && string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
            if (family?.Id == null)
            {
                _logger.LogError("Maxio product family '{Handle}' was not found on the configured site", _options.ProductFamilyHandle);
                throw new SubscriptionException(502, "Subscription plans are not available right now.");
            }

            var all = new List<Product>();
            var truncated = false;
            for (var page = 1; page <= MaxPlanPages; page++)
            {
                var batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = family.Id.Value.ToString(),
                        Page = page,
                        PerPage = PlanPageSize,
                    }, cancellationToken: ct);
                all.AddRange(batch.Select(b => b.Product));
                if (batch.Count < PlanPageSize) break;
                truncated = page == MaxPlanPages;
            }
            return (all, truncated);
        }, "list plans");

        var plans = products.all
            .Where(p => p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle))
            .Select(p => new SubscriptionPlan(
                p.Handle!, p.Name ?? p.Handle!, p.Description,
                (p.PriceInCents ?? 0) / 100m, p.Interval, p.IntervalUnit?.Value))
            .ToList();
        return new SubscriptionPlans(plans, products.truncated);
    }

    // ------------------------------------------------------------ subscribe

    public Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string? firstName, string? lastName, CancellationToken ct) =>
        Bounded(c => SubscribeCoreAsync(userName, planHandle, firstName, lastName, c), ct);

    private async Task<SubscribeResult> SubscribeCoreAsync(string userName, string planHandle, string? firstName, string? lastName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionException(400, "A plan handle is required.");

        // Invariant: the handle must be one the plans listing offers.
        var offered = await GetPlansCoreAsync(ct);
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan == null)
            throw new SubscriptionException(404, $"Plan '{planHandle}' is not available.");

        var customerId = await EnsureCustomerAsync(userName, firstName, lastName, ct);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = await _db.UserSubscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.BuyerId == userName && s.PlanHandle == plan.Handle, ct);

            if (existing != null)
            {
                var settled = await SettleExistingAsync(existing, ct);
                if (settled != null) return new SubscribeResult(settled, true);
                // existing claim was released (abandoned or canceled upstream); try to claim afresh
                continue;
            }

            var claim = new UserSubscription
            {
                BuyerId = userName,
                PlanHandle = plan.Handle,
                Reference = Guid.NewGuid().ToString("N"),
                Status = UserSubscriptionStatus.Pending,
                CreatedAt = _clock.GetUtcNow(),
            };
            if (!await TryClaimAsync(claim, ct))
                continue; // lost the race: loop and settle whatever the winner left

            // The local store may have lost earlier state (e.g. in-memory DB restart): Maxio is the record, so adopt a live subscription to this plan if one exists.
            var live = await FindLiveSubscriptionAsync(customerId, plan.Handle, ct);
            if (live != null)
                return new SubscribeResult(await RecordActiveAsync(claim, live, ct), true);

            return new SubscribeResult(await CreateSubscriptionAsync(claim, customerId, ct), false);
        }

        throw new SubscriptionException(409, "A subscription to this plan is already being created. Try again shortly.");
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var list = await GuardRead(
            () => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct),
            "list customer subscriptions");
        return list.Select(s => s.Subscription)
            .FirstOrDefault(s => s?.Id != null
                && string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal)
                && s.State != SubscriptionState.Canceled && s.State != SubscriptionState.Expired && s.State != SubscriptionState.FailedToCreate);
    }

    private async Task<SubscriptionSummary> CreateSubscriptionAsync(UserSubscription claim, int customerId, CancellationToken ct)
    {
        SubscriptionResponse response;
        try
        {
            response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = claim.PlanHandle,
                        CustomerId = customerId,
                        Reference = claim.Reference,
                        // Plans need no card on file; invoice-based collection avoids the "no payment method" rejection of automatic collection.
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    },
                },
            }, cancellationToken: ct);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            // Provider refused the call: release the claim.
            await ReleaseSubscriptionClaimAsync(claim);
            string detail = ex.Error.TryGetErrorListResponse1(out var list)
                ? string.Join("; ", list.Errors)
                : $"HTTP {(int)ex.StatusCode}";
            _logger.LogWarning("Maxio refused subscription for plan {Plan}: {StatusCode} {Detail}", claim.PlanHandle, (int)ex.StatusCode, detail);
            throw Translate(ex.StatusCode, ex.Error.TryGetErrorListResponse1(out _) ? detail : null, ex);
        }
        catch (ApiException ex) when (ex is not ResponseDeserializationException)
        {
            await ReleaseSubscriptionClaimAsync(claim);
            _logger.LogError(ex, "Maxio rejected CreateSubscription with {StatusCode}", (int)ex.StatusCode);
            throw Translate(ex.StatusCode, null, ex);
        }
        catch (Exception ex) when (ex is ResponseDeserializationException or SdkConnectionException)
        {
            // Outcome unknown: the subscription may exist at Maxio. Settle it by the reference we sent.
            _logger.LogWarning(ex, "CreateSubscription outcome unknown for claim {Reference}; reconciling", claim.Reference);
            var found = await FindByReferenceAsync(claim.Reference, ct);
            if (found?.Id == null)
            {
                // Still unknown: leave the claim Pending; a later subscribe/read settles it.
                throw new SubscriptionException(202,
                    "Your subscription request was received but its outcome is not confirmed yet. Check my subscriptions shortly.", ex);
            }
            return await RecordActiveAsync(claim, found, ct);
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseSubscriptionClaimAsync(claim);
            _logger.LogError(ex, "Maxio credentials could not be applied");
            throw new SubscriptionException(502, "Subscription service is unavailable.", ex);
        }
        catch (OperationCanceledException)
        {
            // Our own budget or the caller aborted mid-write: outcome unknown, keep the claim Pending.
            _logger.LogWarning("CreateSubscription for claim {Reference} was cancelled; outcome unknown", claim.Reference);
            throw;
        }

        if (response.Subscription?.Id == null)
        {
            _logger.LogError("Maxio CreateSubscription returned no subscription id for claim {Reference}", claim.Reference);
            throw new SubscriptionException(202, "Your subscription request was received but could not be confirmed yet.");
        }
        return await RecordActiveAsync(claim, response.Subscription, ct);
    }

    /// <summary>Returns a summary for a claim that already exists, or null if the claim was released and the caller should reclaim.</summary>
    private async Task<SubscriptionSummary?> SettleExistingAsync(UserSubscription existing, CancellationToken ct)
    {
        var remote = await FindByReferenceAsync(existing.Reference, ct);

        if (remote?.Id != null)
        {
            var state = remote.State;
            if (state == SubscriptionState.Canceled || state == SubscriptionState.Expired || state == SubscriptionState.FailedToCreate)
            {
                // Ended upstream: free the key so the user can subscribe again.
                await DeleteSubscriptionClaimAsync(existing.BuyerId, existing.PlanHandle, existing.Reference);
                return null;
            }
            if (existing.Status != UserSubscriptionStatus.Active)
                return await RecordActiveAsync(existing, remote, ct);
            return Summarize(remote);
        }

        // Not at Maxio.
        if (existing.Status == UserSubscriptionStatus.Pending
            && _clock.GetUtcNow() - existing.CreatedAt < StaleClaimAfter)
        {
            throw new SubscriptionException(409, "A subscription to this plan is already being created. Try again shortly.");
        }
        await DeleteSubscriptionClaimAsync(existing.BuyerId, existing.PlanHandle, existing.Reference);
        return null;
    }

    private async Task<SubscriptionSummary> RecordActiveAsync(UserSubscription claim, Subscription remote, CancellationToken ct)
    {
        try
        {
            var row = await _db.UserSubscriptions.FirstAsync(s => s.BuyerId == claim.BuyerId && s.PlanHandle == claim.PlanHandle, CancellationToken.None);
            row.Status = UserSubscriptionStatus.Active;
            row.MaxioSubscriptionId = remote.Id;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The subscription exists at Maxio and is discoverable by reference; a failed local update is repairable.
            _logger.LogError(ex, "Could not record subscription {SubscriptionId} locally", remote.Id);
            _db.ChangeTracker.Clear();
        }
        _logger.LogInformation("Subscribed {Buyer} to {Plan}: Maxio subscription {SubscriptionId} is {State}",
            claim.BuyerId, claim.PlanHandle, remote.Id, remote.State?.Value);
        return Summarize(remote);
    }

    private async Task<bool> TryClaimAsync(UserSubscription claim, CancellationToken ct)
    {
        _db.UserSubscriptions.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task ReleaseSubscriptionClaimAsync(UserSubscription claim) =>
        await DeleteSubscriptionClaimAsync(claim.BuyerId, claim.PlanHandle, claim.Reference);

    private async Task DeleteSubscriptionClaimAsync(string buyerId, string planHandle, string reference)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.UserSubscriptions.FirstOrDefaultAsync(
            s => s.BuyerId == buyerId && s.PlanHandle == planHandle && s.Reference == reference, CancellationToken.None);
        if (row == null) return;
        _db.UserSubscriptions.Remove(row);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------- customer

    private async Task<int> EnsureCustomerAsync(string userName, string? firstName, string? lastName, CancellationToken ct)
    {
        var reference = CustomerReferencePrefix + userName;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.BuyerId == userName, ct);
            if (link?.MaxioCustomerId != null) return link.MaxioCustomerId.Value;

            if (link == null)
            {
                var claim = new MaxioCustomerLink { BuyerId = userName, Reference = reference, CreatedAt = _clock.GetUtcNow() };
                _db.MaxioCustomerLinks.Add(claim);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (IsDuplicateKey(ex))
                {
                    _db.ChangeTracker.Clear();
                    continue; // lost the race; re-read the winner's row
                }
                // The customer may already exist at Maxio (e.g. the local link was lost): reuse it rather than create a duplicate.
                var preexisting = await ReadCustomerByReferenceAsync(reference, ct);
                if (preexisting?.Id != null)
                {
                    await SetCustomerIdAsync(userName, preexisting.Id.Value);
                    return preexisting.Id.Value;
                }
                return await CreateCustomerAsync(userName, reference, firstName, lastName, ct);
            }

            // A claim exists without a customer id: another caller is creating it, or a previous outcome was unknown.
            var remote = await ReadCustomerByReferenceAsync(reference, ct);
            if (remote?.Id != null)
            {
                await SetCustomerIdAsync(userName, remote.Id.Value);
                return remote.Id.Value;
            }
            if (_clock.GetUtcNow() - link.CreatedAt < StaleClaimAfter)
                throw new SubscriptionException(409, "Your billing profile is being created. Try again shortly.");

            // Abandoned claim and no customer at Maxio: take it over.
            await DeleteCustomerClaimAsync(userName);
        }
        throw new SubscriptionException(409, "Your billing profile is being created. Try again shortly.");
    }

    private async Task<int> CreateCustomerAsync(string userName, string reference, string? firstName, string? lastName, CancellationToken ct)
    {
        var email = await ResolveEmailAsync(userName);
        var local = email.Split('@')[0];
        CustomerResponse response;
        try
        {
            response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(firstName) ? local : firstName.Trim(),
                        LastName = string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName.Trim(),
                        Email = email,
                        Reference = reference,
                    },
                },
            }, cancellationToken: ct);
        }
        catch (ApiException ex) when (ex is not ResponseDeserializationException)
        {
            await DeleteCustomerClaimAsync(userName);
            _logger.LogWarning("Maxio refused CreateCustomer with {StatusCode}", (int)ex.StatusCode);
            throw Translate(ex.StatusCode, ex.StatusCode == HttpStatusCode.UnprocessableEntity
                ? "Your billing profile details were rejected. Provide a valid first name, last name and email." : null, ex);
        }
        catch (Exception ex) when (ex is ResponseDeserializationException or SdkConnectionException)
        {
            _logger.LogWarning(ex, "CreateCustomer outcome unknown for {Reference}; reconciling", reference);
            var found = await ReadCustomerByReferenceAsync(reference, ct);
            if (found?.Id == null)
                throw new SubscriptionException(503, "Billing profile creation could not be confirmed. Try again shortly.", ex);
            await SetCustomerIdAsync(userName, found.Id.Value);
            return found.Id.Value;
        }
        catch (AuthSchemeException ex)
        {
            await DeleteCustomerClaimAsync(userName);
            _logger.LogError(ex, "Maxio credentials could not be applied");
            throw new SubscriptionException(502, "Subscription service is unavailable.", ex);
        }

        var id = response.Customer.Id;
        if (id == null) throw new SubscriptionException(502, "Subscription service returned an unexpected response.");
        await SetCustomerIdAsync(userName, id.Value);
        return id.Value;
    }

    private async Task<string> ResolveEmailAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email;
        if (!string.IsNullOrWhiteSpace(email)) return email;
        if (userName.Contains('@')) return userName;
        throw new SubscriptionException(422, "Your account has no email address, which is required for billing.");
    }

    private async Task SetCustomerIdAsync(string buyerId, int customerId)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.BuyerId == buyerId, CancellationToken.None);
        if (row == null) return;
        row.MaxioCustomerId = customerId;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task DeleteCustomerClaimAsync(string buyerId)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.BuyerId == buyerId && l.MaxioCustomerId == null, CancellationToken.None);
        if (row == null) return;
        _db.MaxioCustomerLinks.Remove(row);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------ my subscriptions

    public Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(string userName, CancellationToken ct) =>
        Bounded(c => GetMySubscriptionsCoreAsync(userName, c), ct);

    private async Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsCoreAsync(string userName, CancellationToken ct)
    {
        var customer = await ReadCustomerByReferenceAsync(CustomerReferencePrefix + userName, ct);
        if (customer?.Id == null) return Array.Empty<SubscriptionSummary>();

        var list = await GuardRead(
            () => _client.Customers.ListCustomerSubscriptions(new ListCustomerSubscriptionsRequest { CustomerId = customer.Id.Value }, cancellationToken: ct),
            "list customer subscriptions");
        return list.Where(s => s.Subscription?.Id != null).Select(s => Summarize(s.Subscription!)).ToList();
    }

    // -------------------------------------------------------------- lookups

    /// <summary>Null when Maxio has no such customer (404).</summary>
    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await GuardRead(async () =>
            {
                try
                {
                    return (await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct)).Customer;
                }
                catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
            }, "read customer");
        }
        catch (SubscriptionException) { throw; }
    }

    /// <summary>Null when Maxio has no subscription with that reference.</summary>
    private async Task<Subscription?> FindByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            return (await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct)).Subscription;
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is ApiException or SdkConnectionException or AuthSchemeException)
        {
            _logger.LogWarning(ex, "Could not look up subscription by reference {Reference}", reference);
            throw new SubscriptionException(503, "Subscription status could not be confirmed. Try again shortly.", ex);
        }
    }

    // -------------------------------------------------------------- helpers

    // EF providers report a second insert of the same key differently (SQL Server: DbUpdateException; in-memory: InvalidOperationException/ArgumentException).
    private static bool IsDuplicateKey(Exception ex) => ex is DbUpdateException or InvalidOperationException or ArgumentException;

    private static SubscriptionSummary Summarize(Subscription s) => new(
        s.Id ?? 0,
        s.Product?.Handle,
        s.Product?.Name,
        (s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0) / 100m,
        s.State?.Value,
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        s.Reference);

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
            throw new SubscriptionException(504, "The subscription service did not respond in time.");
        }
    }

    /// <summary>Runs a read, translating any SDK failure into a caller-safe <see cref="SubscriptionException"/>.</summary>
    private async Task<T> GuardRead<T>(Func<Task<T>> read, string what)
    {
        try
        {
            return await read();
        }
        catch (SubscriptionException) { throw; }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Maxio timed out while trying to {What}", what);
            throw new SubscriptionException(504, "The subscription service did not respond in time.", ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Maxio unreachable while trying to {What}", what);
            throw new SubscriptionException(502, "The subscription service is unavailable.", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials could not be applied while trying to {What}", what);
            throw new SubscriptionException(502, "The subscription service is unavailable.", ex);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Maxio returned {StatusCode} while trying to {What}", (int)ex.StatusCode, what);
            throw Translate(ex.StatusCode, null, ex);
        }
    }

    /// <summary>
    /// 401/403/429 are our credentials or quota, not the caller's fault; other 4xx are relayed as 4xx; everything else is 502.
    /// </summary>
    private static SubscriptionException Translate(HttpStatusCode status, string? detail, Exception inner)
    {
        var code = (int)status;
        if (code is 401 or 403) return new SubscriptionException(502, "The subscription service is unavailable.", inner);
        if (code == 429) return new SubscriptionException(503, "The subscription service is busy. Try again shortly.", inner);
        if (code is 404 or 409 or 422) return new SubscriptionException(code == 404 ? 502 : code, detail ?? "The subscription request was rejected.", inner);
        if (code is >= 400 and < 500) return new SubscriptionException(400, detail ?? "The subscription request was rejected.", inner);
        return new SubscriptionException(502, "The subscription service is unavailable.", inner);
    }
}
