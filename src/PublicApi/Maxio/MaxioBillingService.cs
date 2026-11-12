using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Rq = MaxioAdvancedBilling.Requests;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Application layer over the Maxio SDK. Anything it cannot carry out is a <see cref="MaxioFailure"/> (4xx/5xx + reason).
/// </summary>
public partial class MaxioBillingService
{
    private const int PageSize = 200;
    private const int MaxPages = 10;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(2);
    private static readonly Regex BrandFamilyHandle = new("^eshop-brand-(\\d+)$", RegexOptions.Compiled);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, CatalogContext db,
        UserManager<ApplicationUser> users, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _db = db;
        _users = users;
        _logger = logger;
    }

    // ---------------------------------------------------------------- error boundary

    private static string? Describe(ApiException ex) => ex switch
    {
        ApiException<RawError> r => Trim(r.Error.ReadAsString()),
        ApiException<CreateProductFamilyError> a => a.Error.TryGetErrorListResponse1(out var b) ? string.Join("; ", b.Errors) : null,
        ApiException<CreateProductError> a => a.Error.TryGetErrorListResponse1(out var b) ? string.Join("; ", b.Errors) : null,
        ApiException<CreateSubscriptionError> a => a.Error.TryGetErrorListResponse1(out var b) ? string.Join("; ", b.Errors) : null,
        ApiException<UpdateSubscriptionError> a => a.Error.TryGetErrorListResponse1(out var b) ? string.Join("; ", b.Errors) : null,
        ApiException<PauseSubscriptionError> a => a.Error.TryGetErrorListResponse1(out var b) ? string.Join("; ", b.Errors) : null,
        ApiException<ResumeSubscriptionError> a => a.Error.TryGetErrorListResponse1(out var b) ? string.Join("; ", b.Errors) : null,
        ApiException<ReactivateSubscriptionError> a => a.Error.TryGetErrorListResponse1(out var b) ? string.Join("; ", b.Errors) : null,
        _ => null,
    };

    private static string? Trim(string? s) => s is { Length: > 300 } ? s[..300] : s;

    private MaxioFailure Translate(ApiException ex)
    {
        var status = (int)ex.StatusCode;
        var detail = Describe(ex);
        _logger.LogWarning("Maxio answered {Status} for {Method} {Uri}: {Detail}", status, ex.Method, ex.RequestUri?.AbsolutePath, detail);
        return status switch
        {
            404 => new MaxioFailure(404, "Not found."),
            409 => new MaxioFailure(409, detail ?? "Conflict with the current state."),
            401 or 403 => new MaxioFailure(502, "Billing system unavailable."),
            429 => new MaxioFailure(503, "Billing system is busy, try again later."),
            >= 400 and < 500 => new MaxioFailure(422, detail is { Length: > 0 } ? $"Billing system rejected the request: {detail}" : "Billing system rejected the request."),
            _ => new MaxioFailure(502, "Billing system error."),
        };
    }

    /// <summary>
    /// Runs one SDK call. A transport failure on a write leaves the outcome unknown: when <paramref name="settle"/> is
    /// given it re-reads Maxio and returns what is there (null = nothing found); otherwise the caller is told the outcome is unknown.
    /// </summary>
    private async Task<T> Run<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct, Func<CancellationToken, Task<T?>>? settle = null)
        where T : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        try
        {
            return await call(cts.Token);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError(ex, "Maxio returned an unreadable body ({Status})", (int)ex.StatusCode);
            var s = (int)ex.StatusCode;
            throw s is >= 400 and < 500 and not 401 and not 403 and not 429
                ? new MaxioFailure(s == 404 ? 404 : 422, "Billing system rejected the request.", ex)
                : new MaxioFailure(502, "Billing system returned a response that could not be processed.", ex);
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (ex is SdkConnectionException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "Maxio connection failure or timeout");
            if (settle != null)
            {
                try
                {
                    var found = await settle(ct);
                    if (found != null) return found;
                }
                catch (Exception inner) when (inner is not OperationCanceledException)
                {
                    _logger.LogWarning(inner, "Re-reading Maxio after a failed write also failed");
                }
            }
            var unknown = settle != null ? " The outcome of the change could not be confirmed; check before retrying." : "";
            throw new MaxioFailure(ex is SdkTimeoutException or OperationCanceledException ? 504 : 502, "Billing system did not answer." + unknown, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Maxio credentials missing or rejected");
            throw new MaxioFailure(502, "Billing system unavailable.", ex);
        }
    }

    private static async Task<T?> OrNull<T>(Func<Task<T>> read) where T : class
    {
        try { return await read(); }
        catch (ApiException) { return null; }
    }

    // ---------------------------------------------------------------- claims (duplicate prevention)

    /// <summary>claim -> SDK call (work) -> release.</summary>
    private async Task<T> Claimed<T>(string key, Func<Task<T>> work)
    {
        await TakeClaim(key);
        try
        {
            return await work();
        }
        finally
        {
            await ReleaseClaim(key);
        }
    }

    private async Task TakeClaim(string key)
    {
        const string message = "The same request is already in progress, try again shortly.";
        try
        {
            var existing = await _db.MaxioWriteClaims.FirstOrDefaultAsync(c => c.ClaimKey == key);
            if (existing != null)
            {
                if (existing.CreatedAtUtc > DateTime.UtcNow - StaleClaim) throw new MaxioFailure(409, message);
                _db.MaxioWriteClaims.Remove(existing); // abandoned by a dead process
                await _db.SaveChangesAsync();
            }
            _db.MaxioWriteClaims.Add(new MaxioWriteClaim { ClaimKey = key, CreatedAtUtc = DateTime.UtcNow });
            await _db.SaveChangesAsync(); // the primary key refuses the second claimant
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _db.ChangeTracker.Clear();
            throw new MaxioFailure(409, message, ex);
        }
    }

    private async Task ReleaseClaim(string key)
    {
        try
        {
            _db.ChangeTracker.Clear();
            var row = await _db.MaxioWriteClaims.FirstOrDefaultAsync(c => c.ClaimKey == key);
            if (row != null)
            {
                _db.MaxioWriteClaims.Remove(row);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not release claim {Claim}; it expires after {Minutes} min", key, StaleClaim.TotalMinutes);
        }
    }

    // ---------------------------------------------------------------- plan catalog

    private record CatalogRead(List<PlanDto> Plans, bool Truncated);

    /// <summary>The plans this shop offers: non-archived Maxio products filed under a family that maps to a catalog brand.</summary>
    private async Task<CatalogRead> ReadCatalog(CancellationToken ct)
    {
        var brands = await _db.CatalogBrands.AsNoTracking().ToListAsync(ct);
        var plans = new List<PlanDto>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            var p0 = page;
            var batch = await Run(t => _client.Products.ListProducts(new Rq.Products.ListProductsRequest { Page = p0, PerPage = PageSize }, cancellationToken: t), ct);
            foreach (var item in batch)
            {
                var p = item.Product;
                if (p.ArchivedAt != null || p.Id == null || string.IsNullOrEmpty(p.Handle)) continue;
                var m = BrandFamilyHandle.Match(p.ProductFamily?.Handle ?? "");
                if (!m.Success) continue;
                var brandId = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var brand = brands.FirstOrDefault(b => b.Id == brandId);
                if (brand == null) continue;
                plans.Add(new PlanDto(p.Id.Value, p.Handle, p.Name ?? p.Handle, p.Description,
                    (p.PriceInCents ?? 0) / 100m, brand.Brand, brand.Id));
            }
            if (batch.Count < PageSize) break;
            if (page >= MaxPages) { truncated = true; break; }
        }
        return new CatalogRead(plans, truncated);
    }

    public async Task<PlanListDto> ListPlans(CancellationToken ct)
    {
        var c = await ReadCatalog(ct);
        return new PlanListDto(c.Plans, c.Truncated);
    }

    private async Task<PlanDto> RequireOfferedHandle(string? handle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) throw new MaxioFailure(400, "planHandle is required.");
        var c = await ReadCatalog(ct);
        var plan = c.Plans.FirstOrDefault(p => string.Equals(p.Handle, handle.Trim(), StringComparison.Ordinal));
        if (plan != null) return plan;
        if (c.Truncated) throw new MaxioFailure(503, "The plan catalog could not be fully read, try again later.");
        throw new MaxioFailure(400, $"'{handle}' is not a plan that can be subscribed to.");
    }

    private async Task<PlanDto> RequireOfferedId(int planId, CancellationToken ct)
    {
        var c = await ReadCatalog(ct);
        var plan = c.Plans.FirstOrDefault(p => p.Id == planId);
        if (plan != null) return plan;
        if (c.Truncated) throw new MaxioFailure(503, "The plan catalog could not be fully read, try again later.");
        throw new MaxioFailure(404, "Plan not found.");
    }

    // ---------------------------------------------------------------- customers & ownership

    private async Task<int> EnsureCustomer(string user, CancellationToken ct)
    {
        var existing = await FindCustomerId(user, ct);
        if (existing != null) return existing.Value;

        return await Claimed($"cust:{user}", async () =>
        {
            var again = await FindCustomerId(user, ct); // a concurrent caller may have just created it
            if (again != null) return again.Value;

            var identity = await _users.FindByNameAsync(user);
            var email = string.IsNullOrWhiteSpace(identity?.Email) ? $"{Slug(user)}@example.com" : identity.Email!;
            var created = await Run(t => _client.Customers.CreateCustomer(new Rq.Customers.CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = email.Split('@')[0],
                        LastName = "Shopper",
                        Email = email,
                        Reference = user,
                    }
                }
            }, cancellationToken: t), ct,
            settle: t => OrNull(() => _client.Customers.ReadCustomerByReference(new Rq.Customers.ReadCustomerByReferenceRequest { Reference = user }, cancellationToken: t)));
            return created.Customer.Id ?? throw new MaxioFailure(502, "Billing system returned no customer id.");
        });
    }

    private async Task<int?> FindCustomerId(string user, CancellationToken ct)
    {
        try
        {
            var r = await Run(t => _client.Customers.ReadCustomerByReference(new Rq.Customers.ReadCustomerByReferenceRequest { Reference = user }, cancellationToken: t), ct);
            return r.Customer.Id;
        }
        catch (MaxioFailure f) when (f.Status == 404)
        {
            return null;
        }
    }

    private static string Slug(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    /// <summary>Reads a subscription and proves it belongs to <paramref name="user"/>; anything else is a 404.</summary>
    private async Task<Subscription> GetOwned(int subscriptionId, string user, CancellationToken ct)
    {
        var r = await Run(t => _client.Subscriptions.ReadSubscription(new Rq.Subscriptions.ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: t), ct);
        var reference = r.Subscription.Customer?.Reference;
        if (string.IsNullOrEmpty(reference) || !string.Equals(reference, user, StringComparison.OrdinalIgnoreCase))
            throw new MaxioFailure(404, "Subscription not found.");
        return r.Subscription;
    }

    private static SubscriptionDto ToDto(Subscription s, bool? paid) => new(
        s.Id ?? 0, s.State?.Value ?? "unknown", s.Product?.Handle, s.Product?.Name, paid,
        s.NextAssessmentAt, s.NextProductHandle);

    private static MaxioFailure Conflict(string what, Subscription s) =>
        new(409, $"Cannot {what} a subscription that is {s.State?.Value ?? "in an unknown state"}.");

    /// <summary>Re-reads the subscription; returns the response only when it already shows the wanted outcome.</summary>
    private static async Task<SubscriptionResponse?> Reached(MaxioAdvancedBillingClient client, int id, Func<Subscription, bool> wanted, CancellationToken ct)
    {
        var r = await OrNull(() => client.Subscriptions.ReadSubscription(new Rq.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct));
        return r != null && wanted(r.Subscription) ? r : null;
    }
}
