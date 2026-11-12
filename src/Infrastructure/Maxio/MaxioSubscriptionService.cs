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

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : ISubscriptionService
{
    private const int PER_PAGE = 100;
    private const int MAX_PAGES = 10;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioSettings settings, CatalogContext db,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _db = db;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<SubscriptionPlanList> GetPlansAsync(CancellationToken ct)
    {
        using var cts = Budget(ct);
        return await ReadPlansAsync(cts.Token);
    }

    private async Task<SubscriptionPlanList> ReadPlansAsync(CancellationToken ct)
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
                        PerPage = PER_PAGE
                    }, cancellationToken: ct);

                foreach (var item in products)
                {
                    var p = item.Product;
                    if (p?.Handle is null || p.ArchivedAt is not null) continue;
                    plans.Add(new SubscriptionPlan(p.Handle, p.Name ?? p.Handle, p.Description,
                        (p.PriceInCents ?? 0) / 100m, p.Interval, p.IntervalUnit?.Value));
                }

                if (products.Count < PER_PAGE) break;
                if (page >= MAX_PAGES) { truncated = true; break; }
            }
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "listing plans");
        }
        return new SubscriptionPlanList(plans, truncated);
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscriptionInfo> SubscribeAsync(string userId, string email, string planHandle, CancellationToken ct)
    {
        using var cts = Budget(ct);
        var token = cts.Token;

        // Invariant: the plan must be one this application offers - checked before any claim or write.
        var offered = await ReadPlansAsync(token);
        if (offered.IsTruncated)
        {
            throw new BillingUnavailableException("The plan list could not be read completely; cannot validate the plan.", 503);
        }
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(
                $"Plan '{planHandle}' is not offered. Offered plans: {string.Join(", ", offered.Plans.Select(p => p.Handle))}.");
        }

        var reference = $"eshop:{userId}:{plan.Handle}";

        // Claim first (store-enforced PK), then call Maxio, then record the result.
        var claim = new MaxioSubscriptionLink(userId, plan.Handle, reference);
        _db.MaxioSubscriptionLinks.Add(claim);
        try
        {
            await _db.SaveChangesAsync(token);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            return await HandleExistingClaimAsync(userId, plan, ex, token);
        }

        try
        {
            var customerId = await EnsureCustomerAsync(userId, email, token);
            claim.SetCustomer(customerId);

            SubscriptionResponse response;
            try
            {
                response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customerId,
                            Reference = reference,
                            // Plans do not require a payment method: bill by invoice (remittance), no card capture.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                }, cancellationToken: token);
            }
            catch (Exception ex) when (IsUnknownOutcome(ex))
            {
                // The write may have landed: settle by re-reading Maxio before returning.
                _logger.LogWarning(ex, "CreateSubscription outcome unknown for {Reference}; re-reading", reference);
                var found = await TrySettleAsync(claim, customerId, reference, plan.Handle, CancellationToken.None);
                if (found is null)
                {
                    // The re-read succeeded and found nothing -> not created.
                    await ReleaseClaimAsync(claim);
                    throw new BillingUnavailableException("The subscription could not be created; please retry.", 503, ex);
                }
                return Project(found, plan);
            }

            var sub = response.Subscription
                ?? throw new BillingUnavailableException("Maxio returned no subscription.", 502);
            return await CompleteAsync(claim, customerId, sub, plan);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            await ReleaseClaimAsync(claim);
            var message = ex.Error.Match(
                onErrorListResponse1: e => string.Join("; ", e.Errors),
                otherwise: raw => $"HTTP {(int)raw.StatusCode}");
            _logger.LogWarning("Maxio rejected subscription create ({Status}): {Message}", (int)ex.StatusCode, message);
            throw new BillingRejectedException(message, (int)ex.StatusCode, ex);
        }
        catch (BillingUnavailableException) when (claim.State == SubscriptionLinkState.Unknown)
        {
            throw; // claim intentionally kept for later settlement
        }
        catch (SdkException ex)
        {
            await ReleaseClaimAsync(claim);
            throw Translate(ex, "creating the subscription");
        }
        catch (Exception)
        {
            // Rejections raised by the customer step etc.: release the claim.
            await ReleaseClaimAsync(claim);
            throw;
        }
    }

    private async Task<SubscriptionInfo> HandleExistingClaimAsync(string userId, SubscriptionPlan plan, Exception cause, CancellationToken ct)
    {
        var existing = await _db.MaxioSubscriptionLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanHandle == plan.Handle, ct);
        if (existing is null)
        {
            _logger.LogError(cause, "Claim insert failed for a reason other than a duplicate");
            throw new BillingUnavailableException("Could not record the subscription request.", 503, cause);
        }

        _logger.LogWarning("Duplicate subscribe refused for {PlanHandle} (state {State})", plan.Handle, existing.State);

        if (existing.State == SubscriptionLinkState.Completed && existing.MaxioCustomerId is int cid)
        {
            var subs = await ListCustomerSubsAsync(cid, ct);
            var sub = subs.FirstOrDefault(s => s.Id == existing.MaxioSubscriptionId);
            if (sub is not null) return Project(sub, plan);
            return new SubscriptionInfo(existing.MaxioSubscriptionId, plan.Handle, plan.Name, plan.Price, "unknown", null);
        }

        var stale = DateTimeOffset.UtcNow - existing.CreatedAt > StaleClaimAge;
        if (existing.State == SubscriptionLinkState.Unknown || stale)
        {
            // Settle a prior ambiguous/abandoned attempt by re-reading Maxio.
            var tracked = await _db.MaxioSubscriptionLinks.FirstAsync(l => l.UserId == userId && l.PlanHandle == plan.Handle, ct);
            var customerId = tracked.MaxioCustomerId ?? await FindCustomerIdAsync(userId, ct);
            var found = customerId is int c
                ? await TrySettleAsync(tracked, c, tracked.Reference, plan.Handle, ct)
                : null;
            if (found is not null) return Project(found, plan);
            _db.MaxioSubscriptionLinks.Remove(tracked);
            await _db.SaveChangesAsync(ct);
            throw new BillingUnavailableException("A previous attempt did not complete and was cleared; please retry.", 503);
        }

        throw new DuplicateException($"A subscription to plan '{plan.Handle}' is already being created for this user.");
    }

    private async Task<SubscriptionInfo> CompleteAsync(MaxioSubscriptionLink claim, int customerId, Subscription sub, SubscriptionPlan plan)
    {
        if (sub.Id is int id)
        {
            claim.Complete(customerId, id);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        _logger.LogInformation("Subscribed user to {PlanHandle}: Maxio subscription {SubscriptionId}, customer {CustomerId}",
            plan.Handle, sub.Id, customerId);
        return Project(sub, plan);
    }

    /// <summary>Re-reads Maxio for the claim's subscription. Returns null only when Maxio confirmed there is none.
    /// If the re-read itself fails the claim is marked Unknown and BillingUnavailableException is thrown.</summary>
    private async Task<Subscription?> TrySettleAsync(MaxioSubscriptionLink claim, int customerId, string reference, string planHandle, CancellationToken ct)
    {
        try
        {
            Subscription? found = null;
            try
            {
                var byRef = await _client.Subscriptions.FindSubscription(
                    new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
                found = byRef.Subscription;
            }
            catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
            {
                // not found by reference; fall back to the customer's subscriptions
            }

            found ??= (await ListCustomerSubsAsync(customerId, ct))
                .FirstOrDefault(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal)
                                     && s.State != SubscriptionState.Canceled);

            if (found?.Id is int id)
            {
                claim.Complete(customerId, id);
                _db.Update(claim);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            return found;
        }
        catch (Exception ex) when (ex is SdkException or BillingUnavailableException or BillingRejectedException)
        {
            claim.MarkUnknown();
            _db.Update(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Subscription outcome unknown for {Reference}; claim kept in Unknown state", reference);
            throw new BillingUnavailableException("The subscription outcome is unknown; it will be settled on the next request.", 503, ex);
        }
    }

    private async Task ReleaseClaimAsync(MaxioSubscriptionLink claim)
    {
        if (_db.Entry(claim).State == EntityState.Detached) return; // already released
        try
        {
            _db.MaxioSubscriptionLinks.Remove(claim);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release subscription claim");
        }
    }

    // ------------------------------------------------------------- customer

    private async Task<int> EnsureCustomerAsync(string userId, string email, CancellationToken ct)
    {
        var link = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId, ct);
        if (link?.MaxioCustomerId is int known) return known;

        var mayCreate = false;
        if (link is null)
        {
            link = new MaxioCustomerLink(userId);
            _db.MaxioCustomerLinks.Add(link);
            try
            {
                await _db.SaveChangesAsync(ct);
                mayCreate = true; // we won the claim
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
            {
                _db.Entry(link).State = EntityState.Detached;
                link = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId, ct);
                if (link?.MaxioCustomerId is int winnerId) return winnerId;
                if (link is null) throw new BillingUnavailableException("Could not record the customer.", 503, ex);
            }
        }

        // Re-read by reference first: covers a lost response or an abandoned earlier attempt.
        var existingId = await FindCustomerIdAsync(userId, ct);
        if (existingId is null)
        {
            if (!mayCreate)
            {
                throw new DuplicateException("The billing customer for this user is being created; retry shortly.");
            }
            existingId = await CreateCustomerAsync(link, userId, email, ct);
        }

        link.SetMaxioCustomerId(existingId.Value);
        await _db.SaveChangesAsync(CancellationToken.None);
        return existingId.Value;
    }

    private async Task<int?> FindCustomerIdAsync(string userId, CancellationToken ct)
    {
        try
        {
            var r = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = userId }, cancellationToken: ct);
            return r.Customer?.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "looking up the customer");
        }
    }

    private async Task<int> CreateCustomerAsync(MaxioCustomerLink link, string userId, string email, CancellationToken ct)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        try
        {
            var r = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local,
                        LastName = "Customer",
                        Email = email,
                        Reference = userId
                    }
                }
            }, cancellationToken: ct);
            return r.Customer?.Id ?? throw new BillingUnavailableException("Maxio returned no customer.", 502);
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            _db.MaxioCustomerLinks.Remove(link);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new BillingRejectedException("Maxio rejected the customer details.", (int)ex.StatusCode, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            var id = await FindCustomerIdAsync(userId, CancellationToken.None); // settle in the catch
            if (id is int found) return found;
            _db.MaxioCustomerLinks.Remove(link);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new BillingUnavailableException("The customer could not be created; please retry.", 503, ex);
        }
    }

    // -------------------------------------------------------- my subscriptions

    public async Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(string userId, CancellationToken ct)
    {
        using var cts = Budget(ct);
        var token = cts.Token;

        var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, token);
        if (link?.MaxioCustomerId is not int customerId) return Array.Empty<SubscriptionInfo>();

        var offered = await ReadPlansAsync(token);
        var byHandle = offered.Plans.ToDictionary(p => p.Handle);

        var subs = await ListCustomerSubsAsync(customerId, token);
        return subs
            .Where(s => s.Product?.Handle is string h && byHandle.ContainsKey(h))
            .Select(s => Project(s, byHandle[s.Product!.Handle!]))
            .ToList();
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var list = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
            return list.Select(r => r.Subscription).OfType<Subscription>().ToList();
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "listing subscriptions");
        }
    }

    // --------------------------------------------------------------- helpers

    private static SubscriptionInfo Project(Subscription s, SubscriptionPlan plan) =>
        new(s.Id, plan.Handle, plan.Name,
            s.ProductPriceInCents is long c ? c / 100m : plan.Price,
            s.State?.Value ?? "unknown",
            s.NextAssessmentAt);

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    /// <summary>True when a write may have reached Maxio even though we have no usable answer.</summary>
    private static bool IsUnknownOutcome(Exception ex) => ex switch
    {
        SdkConnectionException => true,   // includes SdkTimeoutException
        ResponseDeserializationException r => (int)r.StatusCode is >= 200 and < 300 || (int)r.StatusCode >= 500,
        ApiException a => (int)a.StatusCode >= 500,
        _ => false
    };

    /// <summary>Maps an SDK failure to a caller-safe application exception (no SDK type names leak).</summary>
    private Exception Translate(Exception ex, string what)
    {
        _logger.LogError(ex, "Maxio call failed while {What}", what);
        if (ex is ApiException api)
        {
            var status = (int)api.StatusCode;
            if (status is 401 or 403) return new BillingUnavailableException("Billing provider unavailable.", 502, ex);
            if (status == 429) return new BillingUnavailableException("Billing provider busy; try again later.", 503, ex);
            if (status is >= 400 and < 500 && ex is not ResponseDeserializationException)
                return new BillingRejectedException($"Billing provider rejected the request while {what}.", status, ex);
        }
        return new BillingUnavailableException($"Billing provider unavailable while {what}.", 502, ex);
    }
}
