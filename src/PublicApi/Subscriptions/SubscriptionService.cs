using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Subscribe / list-plans / my-subscriptions on top of Maxio Advanced Billing.
/// Order of every write: claim (store insert, PK = user id) -> Maxio call -> record the result.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const int PlansPageSize = 100;
    private const int MaxPlanPages = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, MaxioSettings settings, CatalogContext db,
        UserManager<ApplicationUser> users, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _db = db;
        _users = users;
        _logger = logger;
    }

    // ---------------------------------------------------------------- plans

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        using var cts = Budget(ct);
        var products = await ListProductsAsync(cts.Token);
        return products.Select(ToPlanDto).ToList();
    }

    /// <summary>Walks every page of the configured family's plans; throws rather than return a partial list.</summary>
    private async Task<List<Product>> ListProductsAsync(CancellationToken ct)
    {
        var all = new List<Product>();
        for (var page = 1; page <= MaxPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PlansPageSize
                    }, cancellationToken: ct);
            }
            catch (SdkException ex)
            {
                throw Translate(ex, "list plans");
            }

            foreach (var item in batch)
            {
                if (item.Product != null && item.Product.ArchivedAt == null) all.Add(item.Product);
            }
            if (batch.Count < PlansPageSize) return all;
        }

        throw new SubscriptionException((int)HttpStatusCode.BadGateway,
            "The plan catalog is too large to be read completely; refusing to return a partial list.");
    }

    // ------------------------------------------------------------ subscribe

    public async Task<SubscribeResult> SubscribeAsync(ClaimsPrincipal caller, string? planHandle, CancellationToken ct)
    {
        var user = await ResolveUserAsync(caller);
        using var cts = Budget(ct);
        var token = cts.Token;

        // Cross-operation invariant: the plan must be one the configured family offers.
        planHandle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionException(400, "planHandle is required.");
        var plans = await ListProductsAsync(token);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan == null)
            throw new SubscriptionException(400,
                $"Unknown plan '{planHandle}'. Available plans: {string.Join(", ", plans.Select(p => p.Handle))}.");

        // CLAIM: the primary key on UserId refuses a second concurrent claim before Maxio is ever called.
        var claim = new SubscriptionClaim(user.Id, planHandle);
        _db.SubscriptionClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            // The store refused the second claim for this user (PK on UserId; the in-memory
            // provider reports it as a raw ArgumentException).
            _db.ChangeTracker.Clear();
            return await ResolveExistingAsync(user.Id, token);
        }

        // SDK calls (customer, then subscription).
        try
        {
            // After an in-memory restart the claim row is gone but Maxio may already hold the subscription.
            var existing = await FindByReferenceAsync(user.Id, token);
            if (existing != null)
            {
                await CompleteAsync(claim, existing);
                return new SubscribeResult(ToDto(existing), false);
            }

            var customerId = await EnsureCustomerAsync(user, token);

            SubscriptionResponse created;
            try
            {
                created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = user.Id,
                            // No payment profile is collected: bill by invoice (remittance) so the plans
                            // that do not require a payment method are accepted.
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    }
                }, cancellationToken: token);
            }
            catch (Exception ex) when (IsUnknownOutcome(ex))
            {
                // The write may have landed: settle it here by re-reading with the reference we sent.
                var found = await TrySettleAsync(user.Id, token);
                if (found == null) throw;
                created = found;
            }

            var sub = created.Subscription ?? throw new SubscriptionException(502, "Billing returned no subscription.");
            await CompleteAsync(claim, sub);
            _logger.LogInformation("Subscription {SubscriptionId} ({Plan}) created for user {UserId}, state {State}",
                sub.Id, planHandle, user.Id, sub.State?.Value);
            return new SubscribeResult(ToDto(sub), true);
        }
        catch (SubscriptionException)
        {
            await ReleaseClaimAsync(claim);
            throw;
        }
        catch (SdkException ex)
        {
            if (IsUnknownOutcome(ex))
            {
                // Outcome still unknown: keep the claim (Claiming); a repeat POST / GET settles it by reference.
                _logger.LogError(ex, "Subscribe outcome unknown for user {UserId}; claim kept", user.Id);
                throw new SubscriptionException(409,
                    "The outcome of the subscription request is not yet known; it is being reconciled. Retry shortly.", ex);
            }
            await ReleaseClaimAsync(claim);
            throw Translate(ex, "create subscription");
        }
        catch
        {
            await ReleaseClaimAsync(claim);
            throw;
        }
    }

    private async Task<SubscribeResult> ResolveExistingAsync(string userId, CancellationToken ct)
    {
        var row = await _db.SubscriptionClaims.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (row?.MaxioSubscriptionId != null)
        {
            var sub = await ReadSubscriptionAsync(row.MaxioSubscriptionId.Value, ct);
            return new SubscribeResult(ToDto(sub), false);
        }

        // Still claiming: either a concurrent request is mid-flight, or an earlier outcome was unknown.
        var found = await TrySettleAsync(userId, ct);
        if (found?.Subscription != null)
        {
            await CompleteByIdAsync(userId, found.Subscription);
            return new SubscribeResult(ToDto(found.Subscription), false);
        }

        throw new SubscriptionException(409, "A subscription request for this account is already in progress.");
    }

    private async Task<int> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceAsync(user.Id, ct);
        if (existing?.Id != null) return existing.Id.Value;

        var email = user.Email ?? user.UserName ?? user.Id;
        var local = email.Split('@')[0];
        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                        LastName = "Shopper",
                        Email = email,
                        Reference = user.Id
                    }
                }
            }, cancellationToken: ct);
            return created.Customer?.Id ?? throw new SubscriptionException(502, "Billing returned no customer id.");
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            // Settle by re-reading with the reference we sent.
            var found = await ReadCustomerByReferenceAsync(user.Id, ct);
            if (found?.Id != null) return found.Id.Value;
            throw;
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var r = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return r.Customer;
        }
        catch (ApiException ex) when (ex is not ResponseDeserializationException && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // only a genuine 404 means "absent"
        }
    }

    // ------------------------------------------------------ my-subscriptions

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal caller, CancellationToken ct)
    {
        var user = await ResolveUserAsync(caller);
        using var cts = Budget(ct);
        var token = cts.Token;
        try
        {
            var row = await _db.SubscriptionClaims.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == user.Id, token);
            if (row?.MaxioSubscriptionId != null)
            {
                return new[] { ToDto(await ReadSubscriptionAsync(row.MaxioSubscriptionId.Value, token)) };
            }

            if (row != null) // Claiming: try to settle an unknown outcome
            {
                var found = await TrySettleAsync(user.Id, token);
                if (found?.Subscription != null)
                {
                    await CompleteByIdAsync(user.Id, found.Subscription);
                    return new[] { ToDto(found.Subscription) };
                }
                return Array.Empty<SubscriptionDto>();
            }

            // No mapping row (e.g. in-memory restart): fall back to Maxio's customer-by-reference.
            var customer = await ReadCustomerByReferenceAsync(user.Id, token);
            if (customer?.Id == null) return Array.Empty<SubscriptionDto>();
            var list = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customer.Id.Value }, cancellationToken: token);
            return list.Where(s => s.Subscription != null).Select(s => ToDto(s.Subscription!)).ToList();
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "read subscriptions");
        }
    }

    // --------------------------------------------------------------- helpers

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal caller)
    {
        var name = caller.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(name) ? null : await _users.FindByNameAsync(name);
        return user ?? throw new SubscriptionException(401, "Unknown caller.");
    }

    private async Task<Subscription> ReadSubscriptionAsync(int id, CancellationToken ct)
    {
        try
        {
            var r = await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct);
            return r.Subscription ?? throw new SubscriptionException(502, "Billing returned no subscription.");
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "read subscription");
        }
    }

    private async Task<Subscription?> FindByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var r = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return r.Subscription;
        }
        catch (ApiException ex) when (ex is not ResponseDeserializationException && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "find subscription");
        }
    }

    /// <summary>Re-reads by reference; null when absent OR when the re-read itself fails (outcome stays unknown).</summary>
    private async Task<SubscriptionResponse?> TrySettleAsync(string reference, CancellationToken ct)
    {
        try
        {
            var sub = await FindByReferenceAsync(reference, ct);
            return sub == null ? null : new SubscriptionResponse { Subscription = sub };
        }
        catch (Exception ex) when (ex is SubscriptionException or SdkException)
        {
            _logger.LogWarning(ex, "Could not reconcile subscription for {Reference}", reference);
            return null;
        }
    }

    private async Task CompleteAsync(SubscriptionClaim claim, Subscription sub)
    {
        claim.Complete(sub.Customer?.Id, sub.Id ?? throw new SubscriptionException(502, "Billing returned no subscription id."));
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CompleteByIdAsync(string userId, Subscription sub)
    {
        var row = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.UserId == userId);
        if (row != null && sub.Id != null)
        {
            row.Complete(sub.Customer?.Id, sub.Id.Value);
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task ReleaseClaimAsync(SubscriptionClaim claim)
    {
        try
        {
            _db.ChangeTracker.Clear();
            var row = await _db.SubscriptionClaims.FirstOrDefaultAsync(c => c.UserId == claim.UserId);
            if (row != null && row.Status == SubscriptionClaim.StatusClaiming)
            {
                _db.SubscriptionClaims.Remove(row);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release subscription claim for {UserId}", claim.UserId);
        }
    }

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    /// <summary>Connection/timeout failures, and an unreadable 2xx, mean the write may have landed.</summary>
    private static bool IsUnknownOutcome(Exception ex) =>
        ex is SdkConnectionException ||
        (ex is ResponseDeserializationException r && (int)r.StatusCode is >= 200 and < 300);

    private SubscriptionException Translate(Exception ex, string what)
    {
        switch (ex)
        {
            case ApiException api:
                var status = (int)api.StatusCode;
                var detail = Describe(api);
                _logger.LogWarning("Maxio {What} failed with {Status}: {Detail}", what, status, detail);
                if (status is 401 or 403) return new SubscriptionException(502, "Billing provider unavailable.", ex);
                if (status == 429) return new SubscriptionException(503, "Billing provider is busy; try again later.", ex);
                if (ex is not ResponseDeserializationException && status is >= 400 and < 500)
                    return new SubscriptionException(status, $"Billing provider rejected the request to {what}: {detail}", ex);
                return new SubscriptionException(502, "Billing provider failed.", ex);
            case SdkTimeoutException:
                _logger.LogError(ex, "Maxio {What} timed out", what);
                return new SubscriptionException(504, "Billing provider did not answer in time.", ex);
            default:
                _logger.LogError(ex, "Maxio {What} failed", what);
                return new SubscriptionException(502, "Billing provider failed or is unreachable.", ex);
        }
    }

    private static string Describe(ApiException ex)
    {
        if (ex is ApiException<CreateSubscriptionError> cs && cs.Error.TryGetErrorListResponse1(out var list) && list != null)
            return string.Join("; ", list.Errors);
        if (ex is ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError> raw)
        {
            var s = raw.Error.ReadAsString();
            return s.Length > 300 ? s[..300] : s;
        }
        return ex.StatusCode.ToString();
    }

    private static SubscriptionPlanDto ToPlanDto(Product p) => new()
    {
        Handle = p.Handle ?? string.Empty,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Price = p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : null,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value
    };

    private static SubscriptionDto ToDto(Subscription s) => new()
    {
        Id = s.Id,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        PriceInCents = s.ProductPriceInCents,
        Price = s.ProductPriceInCents.HasValue ? s.ProductPriceInCents.Value / 100m : null,
        State = s.State?.Value,
        NextBillingDate = s.NextAssessmentAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        CustomerId = s.Customer?.Id
    };
}
