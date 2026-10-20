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
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioModels = MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public record PlanDto(string Handle, string? Name, string? Description, long? PriceInCents, int? Interval, string? IntervalUnit);

public record PlanListResult(IReadOnlyList<PlanDto> Plans, bool IsTruncated);

public record SubscriptionDto(
    int? SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt);

public record SubscriptionListResult(IReadOnlyList<SubscriptionDto> Subscriptions, int Count);

public record SubscribeResult(SubscriptionDto Subscription, bool AlreadySubscribed);

public interface ISubscriptionService
{
    Task<PlanListResult> ListPlansAsync(CancellationToken ct);
    Task<SubscribeResult> SubscribeAsync(string userId, string? email, string planHandle, CancellationToken ct);
    Task<SubscriptionListResult> ListMySubscriptionsAsync(string userId, CancellationToken ct);
}

/// <summary>
/// Subscribe / list use-cases over Maxio Advanced Billing. Every write follows claim -> SDK call -> record:
/// the claim is a row in CatalogContext whose primary key refuses a second concurrent caller before it reaches Maxio.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const int PER_PAGE = 200;
    private const int MAX_PAGES = 10;
    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan _staleClaim = TimeSpan.FromMinutes(2);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, CatalogContext db,
        IOptions<MaxioSettings> settings, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<PlanListResult> ListPlansAsync(CancellationToken ct)
    {
        using var cts = Budget(ct);
        var (products, truncated) = await ReadFamilyProductsAsync(cts.Token);
        var plans = products
            .Where(p => p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle))
            .Select(p => new PlanDto(p.Handle!, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit?.Value))
            .ToList();
        return new PlanListResult(plans, truncated);
    }

    private async Task<(List<MaxioModels.Product> Products, bool Truncated)> ReadFamilyProductsAsync(CancellationToken ct)
    {
        var all = new List<MaxioModels.Product>();
        var truncated = false;
        try
        {
            for (var page = 1; page <= MAX_PAGES; page++)
            {
                var items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PER_PAGE
                    }, cancellationToken: ct);

                all.AddRange(items.Select(i => i.Product));
                if (items.Count < PER_PAGE) break;
                if (page == MAX_PAGES) truncated = true;
            }
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "list plans");
        }
        return (all, truncated);
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(string userId, string? email, string planHandle, CancellationToken ct)
    {
        using var cts = Budget(ct);
        var token = cts.Token;

        // Invariant: the plan must be one this application offers (non-archived, in the configured family).
        var (products, truncated) = await ReadFamilyProductsAsync(token);
        var plan = products.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.Ordinal) && p.ArchivedAt == null);
        if (plan == null)
        {
            if (truncated)
                throw new BillingUnavailableException("The plan catalog could not be fully read; try again later.");
            throw new PlanNotOfferedException($"Plan '{planHandle}' is not offered.");
        }
        if (plan.RequireCreditCard == true)
            throw new PlanNotOfferedException($"Plan '{planHandle}' requires a payment method, which is not supported here.");

        var customerId = await EnsureCustomerAsync(userId, email, token);
        return await EnsureSubscriptionAsync(userId, planHandle, customerId, token);
    }

    private async Task<int> EnsureCustomerAsync(string userId, string? email, CancellationToken ct)
    {
        // CLAIM: primary key (UserId) refuses the second concurrent caller.
        var claim = new MaxioCustomerLink { UserId = userId };
        try
        {
            _db.MaxioCustomerLinks.Add(claim);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsDuplicateClaim(ex))
        {
            _db.Entry(claim).State = EntityState.Detached;
            return await ResolveExistingCustomerClaimAsync(userId, ct);
        }
        _logger.LogInformation("Maxio customer claim won for user {UserId}", userId);

        // Adopt a customer that already exists for this reference (e.g. local store was reset).
        int? existing;
        try
        {
            existing = await TryReadCustomerByReferenceAsync(userId, ct);
        }
        catch (Exception ex) when (ex is BillingUnavailableException)
        {
            await ReleaseAsync(claim); // nothing was written yet
            throw;
        }
        if (existing != null)
            return await RecordCustomerAsync(claim, existing.Value, ct);

        try
        {

            var (first, last) = DeriveNames(email);
            // SDK CALL (POST: never resent by the SDK).
            var created = await _client.Customers.CreateCustomer(
                new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                {
                    Body = new MaxioModels.CreateCustomerRequest
                    {
                        Customer = new MaxioModels.CreateCustomer
                        {
                            FirstName = first,
                            LastName = last,
                            Email = string.IsNullOrWhiteSpace(email) ? $"{userId}@users.invalid" : email,
                            Reference = userId
                        }
                    }
                }, cancellationToken: ct);

            var id = created.Customer.Id
                     ?? throw new BillingUnavailableException("Billing provider returned a customer without an id.");
            _logger.LogInformation("Maxio customer {CustomerId} created for user {UserId}", id, userId);
            return await RecordCustomerAsync(claim, id, ct);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            // Outcome of the write may be unknown (connection failure or 5xx): settle by re-reading by reference.
            if (IsAmbiguous(ex))
            {
                try
                {
                    var found = await TryReadCustomerByReferenceAsync(userId, CancellationToken.None);
                    if (found != null)
                        return await RecordCustomerAsync(claim, found.Value, CancellationToken.None);
                    await ReleaseAsync(claim);
                }
                catch (Exception inner) when (inner is SdkException)
                {
                    _logger.LogWarning("Could not settle unknown customer outcome for user {UserId}; claim kept for reconciliation", userId);
                }
                throw Translate(ex, "create customer");
            }
            await ReleaseAsync(claim);
            throw Translate(ex, "create customer");
        }
        catch (OperationCanceledException)
        {
            // Caller gone / budget spent: the write may or may not have happened; keep the claim and reconcile next time.
            throw;
        }
    }

    private async Task<int> ResolveExistingCustomerClaimAsync(string userId, CancellationToken ct)
    {
        var existing = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct);
        if (existing == null)
            throw new SubscriptionInProgressException("A subscription request for this user is being processed; retry shortly.");
        if (existing.MaxioCustomerId != null)
            return existing.MaxioCustomerId.Value;

        // Unset: either the first caller is in flight, or it died after claiming. Reconcile by reference.
        var found = await TryReadCustomerByReferenceAsync(userId, ct);
        if (found != null)
        {
            var tracked = await _db.MaxioCustomerLinks.FirstAsync(l => l.UserId == userId, ct);
            tracked.MaxioCustomerId = found;
            await _db.SaveChangesAsync(ct);
            return found.Value;
        }
        if (DateTimeOffset.UtcNow - existing.CreatedAt > _staleClaim)
        {
            var stale = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId, ct);
            if (stale != null)
            {
                _db.MaxioCustomerLinks.Remove(stale);
                await _db.SaveChangesAsync(ct);
            }
        }
        throw new SubscriptionInProgressException("A subscription request for this user is being processed; retry shortly.");
    }

    private async Task<int> RecordCustomerAsync(MaxioCustomerLink claim, int customerId, CancellationToken ct)
    {
        claim.MaxioCustomerId = customerId;
        await _db.SaveChangesAsync(ct);
        return customerId;
    }

    private async Task<int?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct);
            return response.Customer.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // a genuine miss; any other failure (incl. unreadable body) is NOT an absence
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "look up customer");
        }
    }

    private async Task<SubscribeResult> EnsureSubscriptionAsync(string userId, string planHandle, int customerId, CancellationToken ct)
    {
        var reference = SubscriptionReference(userId, planHandle);

        // CLAIM: composite primary key (UserId, PlanHandle) refuses the second concurrent caller.
        var claim = new MaxioSubscriptionLink { UserId = userId, PlanHandle = planHandle, Reference = reference };
        try
        {
            _db.MaxioSubscriptionLinks.Add(claim);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsDuplicateClaim(ex))
        {
            _db.Entry(claim).State = EntityState.Detached;
            return await ResolveExistingSubscriptionClaimAsync(userId, planHandle, reference, ct);
        }
        _logger.LogInformation("Maxio subscription claim won for user {UserId}, plan {Plan}", userId, planHandle);

        // Adopt a subscription that already exists for this reference (e.g. local store was reset).
        MaxioModels.Subscription? existing;
        try
        {
            existing = await TryFindSubscriptionAsync(reference, ct);
        }
        catch (Exception ex) when (ex is BillingUnavailableException)
        {
            await ReleaseAsync(claim); // nothing was written yet
            throw;
        }
        if (existing != null)
            return new SubscribeResult(await RecordSubscriptionAsync(claim, existing, ct), true);

        try
        {

            // SDK CALL (POST: never resent by the SDK).
            var created = await _client.Subscriptions.CreateSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new MaxioModels.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioModels.CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            // Invoice-style collection: the plans do not require a payment method on file.
                            PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                        }
                    }
                }, cancellationToken: ct);

            var sub = created.Subscription
                      ?? throw new BillingUnavailableException("Billing provider returned no subscription.");
            _logger.LogInformation("Maxio subscription {SubscriptionId} created for user {UserId}, plan {Plan}", sub.Id, userId, planHandle);
            return new SubscribeResult(await RecordSubscriptionAsync(claim, sub, ct), false);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            if (IsAmbiguous(ex))
            {
                try
                {
                    var found = await TryFindSubscriptionAsync(reference, CancellationToken.None);
                    if (found != null)
                        return new SubscribeResult(await RecordSubscriptionAsync(claim, found, CancellationToken.None), true);
                    await ReleaseAsync(claim);
                }
                catch (Exception inner) when (inner is SdkException)
                {
                    claim.Status = MaxioSubscriptionLinkStatus.Unknown;
                    await _db.SaveChangesAsync(CancellationToken.None);
                    throw new SubscriptionInProgressException("The subscription outcome is being confirmed; retry shortly.");
                }
                throw Translate(ex, "create subscription");
            }
            await ReleaseAsync(claim);
            throw Translate(ex, "create subscription");
        }
    }

    private async Task<SubscribeResult> ResolveExistingSubscriptionClaimAsync(string userId, string planHandle, string reference, CancellationToken ct)
    {
        var existing = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserId == userId && l.PlanHandle == planHandle, ct);
        if (existing == null)
            throw new SubscriptionInProgressException("A subscription request for this plan is being processed; retry shortly.");

        // Created, Unknown or Claimed: Maxio is the record of truth, look it up by our reference.
        var sub = await TryFindSubscriptionAsync(reference, ct);
        if (sub != null)
            return new SubscribeResult(await RecordSubscriptionAsync(existing, sub, ct), true);

        if (existing.Status == MaxioSubscriptionLinkStatus.Unknown
            || DateTimeOffset.UtcNow - existing.CreatedAt > _staleClaim)
        {
            // Settled as "never happened": release so the caller can retry.
            _db.MaxioSubscriptionLinks.Remove(existing);
            await _db.SaveChangesAsync(ct);
            throw new BillingUnavailableException("The previous subscription attempt did not complete; please retry.");
        }
        throw new SubscriptionInProgressException("A subscription request for this plan is being processed; retry shortly.");
    }

    private async Task<SubscriptionDto> RecordSubscriptionAsync(MaxioSubscriptionLink link, MaxioModels.Subscription sub, CancellationToken ct)
    {
        link.MaxioSubscriptionId = sub.Id;
        link.Status = MaxioSubscriptionLinkStatus.Created;
        await _db.SaveChangesAsync(ct);
        return ToDto(sub);
    }

    private async Task<MaxioModels.Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "look up subscription");
        }
    }

    // ------------------------------------------------------ my subscriptions

    public async Task<SubscriptionListResult> ListMySubscriptionsAsync(string userId, CancellationToken ct)
    {
        using var cts = Budget(ct);
        var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, cts.Token);
        if (link?.MaxioCustomerId == null)
            return new SubscriptionListResult(Array.Empty<SubscriptionDto>(), 0);

        // Settle subscribe attempts whose outcome was unknown, so they show up below.
        var pending = await _db.MaxioSubscriptionLinks
            .Where(l => l.UserId == userId && l.Status != MaxioSubscriptionLinkStatus.Created)
            .ToListAsync(cts.Token);
        foreach (var p in pending)
        {
            var sub = await TryFindSubscriptionAsync(p.Reference, cts.Token);
            if (sub != null)
                await RecordSubscriptionAsync(p, sub, cts.Token);
            else if (p.Status == MaxioSubscriptionLinkStatus.Unknown || DateTimeOffset.UtcNow - p.CreatedAt > _staleClaim)
            {
                _db.MaxioSubscriptionLinks.Remove(p);
                await _db.SaveChangesAsync(cts.Token);
            }
        }

        try
        {
            var items = await _client.Customers.ListCustomerSubscriptions(
                new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = link.MaxioCustomerId.Value },
                cancellationToken: cts.Token);
            var dtos = items.Where(i => i.Subscription != null).Select(i => ToDto(i.Subscription!)).ToList();
            return new SubscriptionListResult(dtos, dtos.Count);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw Translate(ex, "list subscriptions");
        }
    }

    // --------------------------------------------------------------- helpers

    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-sub:{userId}:{planHandle}";

    private static SubscriptionDto ToDto(MaxioModels.Subscription s) => new(
        s.Id, s.Product?.Handle, s.Product?.Name, s.ProductPriceInCents, s.State?.Value, s.NextAssessmentAt, s.CurrentPeriodEndsAt);

    private static (string First, string Last) DeriveNames(string? email)
    {
        var local = string.IsNullOrWhiteSpace(email) ? "eShop" : email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(local)) local = "eShop";
        return (local, "Customer");
    }

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        return cts;
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

    /// <summary>
    /// The store refusing a second claim. SQL Server surfaces a DbUpdateException; the EF in-memory provider
    /// surfaces the underlying duplicate-key exception directly. Only the claim insert is pending when this is checked.
    /// </summary>
    private static bool IsDuplicateClaim(Exception ex) =>
        ex is DbUpdateException or ArgumentException or InvalidOperationException;

    /// <summary>A write whose connection failed, timed out, or answered 5xx / unreadably may still have landed.</summary>
    private static bool IsAmbiguous(Exception ex) => ex switch
    {
        SdkConnectionException => true,
        ResponseDeserializationException r => (int)r.StatusCode is >= 200 and < 300 || (int)r.StatusCode >= 500,
        ApiException a => (int)a.StatusCode >= 500,
        _ => false
    };

    /// <summary>One ladder converting SDK failures into application exceptions with caller-safe messages.</summary>
    private Exception Translate(Exception ex, string action)
    {
        switch (ex)
        {
            case ApiException<CreateSubscriptionError> sub:
                _logger.LogWarning("Maxio {Action} failed with {Status}", action, (int)sub.StatusCode);
                if (sub.StatusCode == HttpStatusCode.UnprocessableEntity && sub.Error.TryGetErrorListResponse1(out var list))
                    return new BillingRejectedException("Billing rejected the subscription: " + string.Join("; ", list.Errors));
                return FromStatus(sub.StatusCode, action, sub);
            case ApiException<CreateCustomerError> cust:
                _logger.LogWarning("Maxio {Action} failed with {Status}", action, (int)cust.StatusCode);
                if (cust.StatusCode == HttpStatusCode.UnprocessableEntity)
                    return new BillingRejectedException("Billing rejected the customer details.");
                return FromStatus(cust.StatusCode, action, cust);
            case ResponseDeserializationException de:
                _logger.LogError(de, "Maxio {Action} returned an unreadable body (status {Status})", action, (int)de.StatusCode);
                return new BillingUnavailableException("The billing provider returned a response that could not be processed.", de);
            case ApiException api:
                _logger.LogWarning("Maxio {Action} failed with {Status}", action, (int)api.StatusCode);
                return FromStatus(api.StatusCode, action, api);
            case SdkTimeoutException to:
                _logger.LogError(to, "Maxio {Action} timed out", action);
                return new BillingUnavailableException("The billing provider did not answer in time.", to);
            case SdkConnectionException conn:
                _logger.LogError(conn, "Maxio {Action} could not reach the provider", action);
                return new BillingUnavailableException("The billing provider is unreachable.", conn);
            case AuthSchemeException auth:
                _logger.LogError(auth, "Maxio credentials could not be applied");
                return new BillingUnavailableException("The billing provider is misconfigured.", auth);
            default:
                _logger.LogError(ex, "Maxio {Action} failed", action);
                return new BillingUnavailableException("The billing provider failed.", ex);
        }
    }

    private static Exception FromStatus(HttpStatusCode status, string action, Exception inner)
    {
        // Our credentials or quota (401/403/429) and anything 5xx are not the caller's fault.
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or (HttpStatusCode)429 || (int)status >= 500)
            return new BillingUnavailableException("The billing provider is temporarily unavailable.", inner);
        return new BillingRejectedException($"Billing rejected the request to {action}.");
    }
}
