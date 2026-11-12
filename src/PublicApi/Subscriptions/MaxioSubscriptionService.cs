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
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Subscription flow on top of Maxio. Order for every write: claim (local store) -> SDK call -> record result.
/// </summary>
public class MaxioSubscriptionService
{
    private const int PageSize = 200;
    private const int MaxPages = 10;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(2);

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

    public static string CustomerReference(string userId) => $"eshop-user:{userId}";
    public static string SubscriptionReference(string userId, string plan) => $"eshop-sub:{userId}:{plan}";

    // ---------- plans ----------

    public Task<PlanListResult> ListPlansAsync(CancellationToken ct) =>
        Bounded(token => ListPlansCoreAsync(token), ct);

    private async Task<PlanListResult> ListPlansCoreAsync(CancellationToken ct)
    {
        var plans = new List<MaxioAdvancedBilling.Models.Product>();
        var truncated = false;
        try
        {
            for (var page = 1; ; page++)
            {
                var items = await _client.ProductFamilies.ListProductsForProductFamily(
                    new MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PageSize,
                    }, cancellationToken: ct);
                plans.AddRange(items.Select(i => i.Product));
                if (items.Count < PageSize) break;
                if (page >= MaxPages) { truncated = true; break; }
            }
        }
        catch (ApiException ex) { throw Translate(ex, "list plans"); }
        catch (SdkException ex) { throw TranslateTransport(ex, "list plans"); }

        return new PlanListResult
        {
            IsTruncated = truncated,
            Plans = plans
                .Where(p => p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle))
                .Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Handle!,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents ?? 0,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit?.Value,
                })
                .ToList(),
        };
    }

    // ---------- subscribe ----------

    public Task<SubscribeResult> SubscribeAsync(string userId, string? email, string? planHandle, CancellationToken ct) =>
        Bounded(token => SubscribeCoreAsync(userId, email, planHandle, token), ct);

    private async Task<SubscribeResult> SubscribeCoreAsync(string userId, string? email, string? planHandle, CancellationToken ct)
    {
        // Cross-operation invariant: the plan must be one the family currently offers.
        var offered = await ListPlansCoreAsync(ct);
        planHandle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle.Trim();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            if (offered.Plans.Count == 1) planHandle = offered.Plans[0].Handle;
            else throw new SubscriptionException(400,
                "planHandle is required. Offered plans: " + string.Join(", ", offered.Plans.Select(p => p.Handle)));
        }
        var plan = offered.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan == null)
        {
            if (offered.IsTruncated)
                throw new SubscriptionException(503, "The plan list could not be read completely; try again later.");
            throw new SubscriptionException(400,
                $"Unknown plan '{planHandle}'. Offered plans: " + string.Join(", ", offered.Plans.Select(p => p.Handle)));
        }

        var customerId = await EnsureCustomerAsync(userId, email, ct);
        return await EnsureSubscriptionAsync(userId, plan.Handle, customerId, ct);
    }

    private async Task<int> EnsureCustomerAsync(string userId, string? email, CancellationToken ct)
    {
        var reference = CustomerReference(userId);
        var link = new MaxioCustomerLink { UserId = userId, Reference = reference, ClaimedAt = DateTimeOffset.UtcNow };

        // CLAIM: primary key on UserId refuses the second caller before it reaches Maxio.
        _db.MaxioCustomerLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            _db.Entry(link).State = EntityState.Detached;
            var existing = await _db.MaxioCustomerLinks.AsNoTracking().FirstAsync(l => l.UserId == userId, ct);
            if (existing.MaxioCustomerId.HasValue) return existing.MaxioCustomerId.Value;
            return await SettleInFlightCustomerAsync(userId, reference, email, existing, ct);
        }

        return await CreateCustomerClaimedAsync(link, email, ct);
    }

    private async Task<int> SettleInFlightCustomerAsync(string userId, string reference, string? email,
        MaxioCustomerLink existing, CancellationToken ct)
    {
        var found = await ReadCustomerByReferenceAsync(reference, ct);
        if (found != null)
        {
            await RecordCustomerAsync(userId, found.Value, ct);
            return found.Value;
        }
        if (DateTimeOffset.UtcNow - existing.ClaimedAt < StaleClaim)
            throw new SubscriptionException(409, "A subscription request for this user is already in progress; retry shortly.");

        // Stale claim and Maxio has no such customer: the earlier attempt never landed. Take it over.
        var tracked = await _db.MaxioCustomerLinks.FirstAsync(l => l.UserId == userId, ct);
        tracked.ClaimedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await CreateCustomerClaimedAsync(tracked, email, ct);
    }

    private async Task<int> CreateCustomerClaimedAsync(MaxioCustomerLink link, string? email, CancellationToken ct)
    {
        // Adopt a customer that already exists for this reference (e.g. the local store was reset).
        var adopted = await ReadCustomerByReferenceAsync(link.Reference, ct);
        if (adopted != null)
        {
            await RecordCustomerAsync(link.UserId, adopted.Value, ct);
            return adopted.Value;
        }

        var address = string.IsNullOrWhiteSpace(email) ? link.UserId : email!;
        var local = address.Contains('@') ? address.Substring(0, address.IndexOf('@')) : address;
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateCustomerRequest
                    {
                        Customer = new MaxioAdvancedBilling.Models.CreateCustomer
                        {
                            FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                            LastName = "eShopOnWeb",
                            Email = address,
                            Reference = link.Reference,
                        },
                    },
                }, cancellationToken: ct);

            var id = response.Customer.Id ?? throw new ResponseMissingIdException();
            await RecordCustomerAsync(link.UserId, id, ct);
            return id;
        }
        catch (ApiException<CreateCustomerError> ex) when ((int)ex.StatusCode < 500)
        {
            await ReleaseCustomerClaimAsync(link.UserId);
            var detail = ex.Error.Match(
                onCustomerErrorResponse1: b => b.Errors?.ToString() ?? "validation failed",
                otherwise: raw => $"HTTP {(int)raw.StatusCode}");
            _logger.LogWarning("Maxio refused CreateCustomer: HTTP {Status}", (int)ex.StatusCode);
            throw Translate(ex, "create customer", detail);
        }
        catch (Exception ex) when (ex is SdkConnectionException or ResponseMissingIdException
            or ResponseDeserializationException or ApiException { StatusCode: >= HttpStatusCode.InternalServerError })
        {
            // UNKNOWN OUTCOME: the customer may exist. Settle by re-reading with the reference we sent.
            var found = await ReadCustomerByReferenceAsync(link.Reference, ct, swallow: true);
            if (found != null)
            {
                await RecordCustomerAsync(link.UserId, found.Value, ct);
                return found.Value;
            }
            _logger.LogError(ex, "CreateCustomer outcome unknown; claim kept in flight for reconciliation");
            throw new SubscriptionException(409, "The customer outcome is unknown; retry shortly.", ex);
        }
        catch (ApiException ex) { throw Translate(ex, "create customer"); }
        catch (SdkException ex) { throw TranslateTransport(ex, "create customer"); }
    }

    private async Task<int?> ReadCustomerByReferenceAsync(string reference, CancellationToken ct, bool swallow = false)
    {
        try
        {
            var r = await _client.Customers.ReadCustomerByReference(
                new MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct);
            return r.Customer.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (swallow && ex is SdkException)
        {
            return null;
        }
        catch (ApiException ex) { throw Translate(ex, "read customer"); }
        catch (SdkException ex) { throw TranslateTransport(ex, "read customer"); }
    }

    private async Task RecordCustomerAsync(string userId, int maxioId, CancellationToken ct)
    {
        var row = await _db.MaxioCustomerLinks.FirstAsync(l => l.UserId == userId, ct);
        row.MaxioCustomerId = maxioId;
        await _db.SaveChangesAsync(ct);
    }

    private async Task ReleaseCustomerClaimAsync(string userId)
    {
        var row = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == userId);
        if (row != null && !row.MaxioCustomerId.HasValue)
        {
            _db.MaxioCustomerLinks.Remove(row);
            await _db.SaveChangesAsync();
        }
    }

    private async Task<SubscribeResult> EnsureSubscriptionAsync(string userId, string planHandle, int customerId, CancellationToken ct)
    {
        var reference = SubscriptionReference(userId, planHandle);
        var link = new MaxioSubscriptionLink
        {
            UserId = userId, PlanHandle = planHandle, Reference = reference, ClaimedAt = DateTimeOffset.UtcNow
        };

        // CLAIM: composite primary key (UserId, PlanHandle).
        _db.MaxioSubscriptionLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            _db.Entry(link).State = EntityState.Detached;
            var existing = await _db.MaxioSubscriptionLinks.AsNoTracking()
                .FirstAsync(l => l.UserId == userId && l.PlanHandle == planHandle, ct);

            var found = await FindSubscriptionAsync(reference, ct);
            if (found != null)
            {
                if (!existing.MaxioSubscriptionId.HasValue) await RecordSubscriptionAsync(userId, planHandle, found.Id!.Value, ct);
                return new SubscribeResult { Created = false, Subscription = ToDto(found, planHandle) };
            }
            if (DateTimeOffset.UtcNow - existing.ClaimedAt < StaleClaim)
                throw new SubscriptionException(409, "A subscription request for this plan is already in progress; retry shortly.");

            var tracked = await _db.MaxioSubscriptionLinks.FirstAsync(l => l.UserId == userId && l.PlanHandle == planHandle, ct);
            tracked.ClaimedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            link = tracked;
        }

        return await CreateSubscriptionClaimedAsync(link, customerId, ct);
    }

    private async Task<SubscribeResult> CreateSubscriptionClaimedAsync(MaxioSubscriptionLink link, int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                    {
                        Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                        {
                            ProductHandle = link.PlanHandle,
                            CustomerId = customerId,
                            // Invoice-based collection: no payment profile / card capture needed.
                            PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
                            Reference = link.Reference,
                        },
                    },
                }, cancellationToken: ct);

            var sub = response.Subscription;
            if (sub?.Id == null) throw new ResponseMissingIdException();
            await RecordSubscriptionAsync(link.UserId, link.PlanHandle, sub.Id.Value, ct);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for plan {Plan}", sub.Id, link.PlanHandle);
            return new SubscribeResult { Created = true, Subscription = ToDto(sub, link.PlanHandle) };
        }
        catch (ApiException<CreateSubscriptionError> ex) when ((int)ex.StatusCode < 500)
        {
            await ReleaseSubscriptionClaimAsync(link.UserId, link.PlanHandle);
            var detail = ex.Error.Match(
                onErrorListResponse1: b => string.Join("; ", b.Errors),
                otherwise: raw => $"HTTP {(int)raw.StatusCode}");
            _logger.LogWarning("Maxio refused CreateSubscription: HTTP {Status}", (int)ex.StatusCode);
            throw Translate(ex, "create subscription", detail);
        }
        catch (Exception ex) when (ex is SdkConnectionException or ResponseMissingIdException
            or ResponseDeserializationException or ApiException { StatusCode: >= HttpStatusCode.InternalServerError })
        {
            // UNKNOWN OUTCOME: settle by looking the subscription up by the reference we sent.
            var found = await FindSubscriptionAsync(link.Reference, ct, swallow: true);
            if (found != null)
            {
                await RecordSubscriptionAsync(link.UserId, link.PlanHandle, found.Id!.Value, ct);
                return new SubscribeResult { Created = true, Subscription = ToDto(found, link.PlanHandle) };
            }
            _logger.LogError(ex, "CreateSubscription outcome unknown; claim kept in flight for reconciliation");
            throw new SubscriptionException(409, "The subscription outcome is unknown; retry shortly.", ex);
        }
        catch (ApiException ex) { throw Translate(ex, "create subscription"); }
        catch (SdkException ex) { throw TranslateTransport(ex, "create subscription"); }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct, bool swallow = false)
    {
        try
        {
            var r = await _client.Subscriptions.FindSubscription(
                new MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct);
            return r.Subscription?.Id == null ? null : r.Subscription;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (swallow && ex is SdkException)
        {
            return null;
        }
        catch (ApiException ex) { throw Translate(ex, "find subscription"); }
        catch (SdkException ex) { throw TranslateTransport(ex, "find subscription"); }
    }

    private async Task RecordSubscriptionAsync(string userId, string plan, int maxioId, CancellationToken ct)
    {
        var row = await _db.MaxioSubscriptionLinks.FirstAsync(l => l.UserId == userId && l.PlanHandle == plan, ct);
        row.MaxioSubscriptionId = maxioId;
        await _db.SaveChangesAsync(ct);
    }

    private async Task ReleaseSubscriptionClaimAsync(string userId, string plan)
    {
        var row = await _db.MaxioSubscriptionLinks.FirstOrDefaultAsync(l => l.UserId == userId && l.PlanHandle == plan);
        if (row != null && !row.MaxioSubscriptionId.HasValue)
        {
            _db.MaxioSubscriptionLinks.Remove(row);
            await _db.SaveChangesAsync();
        }
    }

    // ---------- my subscriptions ----------

    public Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userId, CancellationToken ct) =>
        Bounded(token => ListMineCoreAsync(userId, token), ct);

    private async Task<IReadOnlyList<SubscriptionDto>> ListMineCoreAsync(string userId, CancellationToken ct)
    {
        var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct);
        if (link?.MaxioCustomerId == null) return Array.Empty<SubscriptionDto>();
        try
        {
            var list = await _client.Customers.ListCustomerSubscriptions(
                new MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest { CustomerId = link.MaxioCustomerId.Value },
                cancellationToken: ct);
            return list.Where(s => s.Subscription != null)
                .Select(s => ToDto(s.Subscription!, s.Subscription!.Product?.Handle))
                .ToList();
        }
        catch (ApiException ex) { throw Translate(ex, "list subscriptions"); }
        catch (SdkException ex) { throw TranslateTransport(ex, "list subscriptions"); }
    }

    // ---------- helpers ----------

    private static SubscriptionDto ToDto(MaxioAdvancedBilling.Models.Subscription s, string? planHandle) => new()
    {
        SubscriptionId = s.Id,
        PlanHandle = s.Product?.Handle ?? planHandle,
        PlanName = s.Product?.Name,
        PriceInCents = s.ProductPriceInCents ?? s.Product?.PriceInCents,
        State = s.State?.Value,
        NextBillingDate = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        Reference = s.Reference,
    };

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Budget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SubscriptionException(504, "Billing provider did not answer in time.");
        }
    }

    private SubscriptionException Translate(ApiException ex, string what, string? detail = null)
    {
        var code = (int)ex.StatusCode;
        // Our credentials / our quota are not the caller's fault.
        if (code is 401 or 403)
        {
            _logger.LogError("Maxio rejected our credentials during {What} (HTTP {Status})", what, code);
            return new SubscriptionException(502, "Billing provider unavailable.", ex);
        }
        if (code == 429) return new SubscriptionException(503, "Billing provider temporarily unavailable.", ex);
        if (code is >= 400 and < 500)
            return new SubscriptionException(code == 404 ? 404 : 422, $"Billing provider rejected the request ({what}): {detail ?? "HTTP " + code}", ex);
        _logger.LogError(ex, "Maxio error during {What}: HTTP {Status}", what, code);
        return new SubscriptionException(502, "Billing provider error.", ex);
    }

    private SubscriptionException TranslateTransport(SdkException ex, string what)
    {
        _logger.LogError(ex, "Maxio call failed during {What}", what);
        return ex is SdkConnectionException
            ? new SubscriptionException(502, "Billing provider unreachable.", ex)
            : new SubscriptionException(502, "Billing provider error.", ex);
    }

    private sealed class ResponseMissingIdException : Exception { }
}
