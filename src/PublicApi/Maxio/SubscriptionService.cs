using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscribe flow against Maxio (billing system of record).
/// Every write follows: claim (insert in our store, primary key refuses a second claim) -> SDK call -> record result.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const int PlanPageSize = 200;
    private const int PlanPageCap = 5;
    private const int MySubscriptionsCap = 50;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WriteBudget = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

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

    public async Task<PlanList> ListPlansAsync(CancellationToken ct)
    {
        return await Bounded(ReadBudget, ct, async token =>
        {
            var (products, truncated) = await ListProductsAsync(token);
            return new PlanList { Plans = products.Select(ToPlanDto).ToList(), Truncated = truncated };
        });
    }

    private async Task<(List<Product> Products, bool Truncated)> ListProductsAsync(CancellationToken ct)
    {
        var all = new List<Product>();
        var truncated = false;
        for (var page = 1; page <= PlanPageCap; page++)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        // handle-prefixed reference to the configured family; never caller input
                        ProductFamilyId = "handle:" + _settings.ProductFamilyHandle,
                        Page = page,
                        PerPage = PlanPageSize
                    }, cancellationToken: ct);
            }
            catch (SdkException ex)
            {
                throw Translate(ex, "list plans");
            }

            foreach (var item in batch)
            {
                var p = item.Product;
                if (p != null && p.ArchivedAt == null && !string.IsNullOrEmpty(p.Handle))
                    all.Add(p);
            }
            if (batch.Count < PlanPageSize) break;
            if (page == PlanPageCap) truncated = true;
        }
        return (all, truncated);
    }

    private static SubscriptionPlanDto ToPlanDto(Product p) => new()
    {
        Handle = p.Handle!,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents ?? 0,
        Price = (p.PriceInCents ?? 0) / 100m,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit?.Value
    };

    // ---------------------------------------------------------------- subscribe

    public async Task<(SubscriptionDto Subscription, bool Created)> SubscribeAsync(string userName, string? planHandle, CancellationToken ct)
    {
        return await Bounded(WriteBudget, ct, async token =>
        {
            var user = await _users.FindByNameAsync(userName)
                ?? throw new SubscriptionException(HttpStatusCode.Unauthorized, "Unknown user.");

            planHandle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle.Trim();
            if (string.IsNullOrWhiteSpace(planHandle))
                throw new SubscriptionException(HttpStatusCode.BadRequest, "A plan handle is required.");

            // Invariant: the plan must be one this application offers (non-archived product of the configured family).
            var (products, truncated) = await ListProductsAsync(token);
            var product = products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (product == null)
            {
                if (truncated)
                    throw new SubscriptionException(HttpStatusCode.ServiceUnavailable, "The plan catalog could not be fully verified. Try again later.");
                throw new SubscriptionException(HttpStatusCode.NotFound, $"Plan '{planHandle}' is not available.");
            }
            // Payment-method requirements are enforced by Maxio: a refusal surfaces as a 422 from CreateSubscription.
            var handle = product.Handle!;

            var customerId = await EnsureCustomerAsync(user, token);
            return await EnsureSubscriptionAsync(user, handle, customerId, token);
        });
    }

    private async Task<int> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct)
    {
        var link = new MaxioCustomerLink
        {
            UserId = user.Id, Reference = user.Id, Status = ClaimStatus.Pending, ClaimedAtUtc = DateTime.UtcNow
        };
        _db.MaxioCustomerLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(ct); // CLAIM: primary key on UserId refuses a second claim
        }
        catch (Exception ex) when (IsClaimConflict(ex))
        {
            _db.Entry(link).State = EntityState.Detached;
            var existing = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.UserId == user.Id, ct)
                ?? throw new SubscriptionException(HttpStatusCode.Conflict, "Another subscription request is in progress.");
            if (existing.Status == ClaimStatus.Completed && existing.MaxioCustomerId.HasValue)
                return existing.MaxioCustomerId.Value;
            if (existing.Status == ClaimStatus.Pending && DateTime.UtcNow - existing.ClaimedAtUtc < ClaimLease)
                throw new SubscriptionException(HttpStatusCode.Conflict, "Another subscription request is in progress.");

            // Stale or unknown claim: settle from the provider before ever creating again.
            _logger.LogWarning("Settling stale customer claim for user {UserId}", user.Id);
            var found = await FindCustomerAsync(user.Id, ct);
            if (found.HasValue)
            {
                Complete(existing, found.Value);
                await _db.SaveChangesAsync(CancellationToken.None);
                return found.Value;
            }
            existing.Status = ClaimStatus.Pending;
            existing.ClaimedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            link = existing;
        }

        try
        {
            var email = user.Email ?? user.UserName ?? user.Id;
            var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = email.Split('@')[0],
                        LastName = "eShopOnWeb",
                        Email = email,
                        Reference = user.Id
                    }
                }
            }, cancellationToken: ct);
            var id = response.Customer?.Id;
            if (!id.HasValue)
                throw new ResponseNotUsable();
            Complete(link, id.Value);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", id, user.Id);
            return id.Value;
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            // Rejected (e.g. reference already exists from an earlier lost response): reconcile before giving up.
            _logger.LogWarning("Maxio rejected customer create for user {UserId}: HTTP {Status}", user.Id, (int)ex.StatusCode);
            var found = await TryFindCustomerAsync(user.Id, ct);
            if (found.HasValue)
            {
                Complete(link, found.Value);
                await _db.SaveChangesAsync(CancellationToken.None);
                return found.Value;
            }
            if (IsDefiniteRefusal(ex))
                _db.MaxioCustomerLinks.Remove(link);
            else
                link.Status = ClaimStatus.Unknown;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw ex.StatusCode == HttpStatusCode.UnprocessableEntity
                ? new SubscriptionException(HttpStatusCode.UnprocessableEntity, "Maxio rejected the customer details.", ex)
                : Translate(ex, "create customer");
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            // Outcome unknown: the write may have landed. Settle by re-reading with the reference we sent.
            var found = await TryFindCustomerAsync(user.Id, ct);
            if (found.HasValue)
            {
                Complete(link, found.Value);
                await _db.SaveChangesAsync(CancellationToken.None);
                return found.Value;
            }
            link.Status = ClaimStatus.Unknown;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Customer create outcome unknown for user {UserId}", user.Id);
            throw UnknownOutcome(ex);
        }
        catch (SdkException ex)
        {
            if (IsDefiniteRefusal(ex))
                _db.MaxioCustomerLinks.Remove(link);
            else
                link.Status = ClaimStatus.Unknown;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw Translate(ex, "create customer");
        }
    }

    private async Task<(SubscriptionDto Subscription, bool Created)> EnsureSubscriptionAsync(
        ApplicationUser user, string handle, int customerId, CancellationToken ct)
    {
        var reference = $"{user.Id}-{handle}";
        var claim = new MaxioSubscriptionClaim
        {
            UserId = user.Id, PlanHandle = handle, Reference = reference,
            Status = ClaimStatus.Pending, ClaimedAtUtc = DateTime.UtcNow
        };
        _db.MaxioSubscriptionClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct); // CLAIM: composite primary key (UserId, PlanHandle) refuses a second claim
        }
        catch (Exception ex) when (IsClaimConflict(ex))
        {
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.MaxioSubscriptionClaims
                .FirstOrDefaultAsync(c => c.UserId == user.Id && c.PlanHandle == handle, ct)
                ?? throw new SubscriptionException(HttpStatusCode.Conflict, "Another subscription request is in progress.");
            if (existing.Status == ClaimStatus.Completed && existing.MaxioSubscriptionId.HasValue)
            {
                _logger.LogInformation("User {UserId} already subscribed to {Plan}; returning existing", user.Id, handle);
                return (await ReadExistingAsync(existing, ct), false);
            }
            if (existing.Status == ClaimStatus.Pending && DateTime.UtcNow - existing.ClaimedAtUtc < ClaimLease)
                throw new SubscriptionException(HttpStatusCode.Conflict, "Another subscription request is in progress.");

            _logger.LogWarning("Settling stale subscription claim for user {UserId} plan {Plan}", user.Id, handle);
            var found = await FindSubscriptionAsync(reference, ct);
            if (found?.Id != null)
            {
                Complete(existing, found.Id.Value);
                await _db.SaveChangesAsync(CancellationToken.None);
                return (ToDto(found, existing), false);
            }
            existing.Status = ClaimStatus.Pending;
            existing.ClaimedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            claim = existing;
        }

        try
        {
            var response = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = handle,
                        CustomerId = customerId, // from our own link row, never from the request
                        // The seeded plans take no card: bill by invoice (remittance) instead of charging a payment method.
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                        Reference = reference
                    }
                }
            }, cancellationToken: ct);
            var sub = response.Subscription;
            if (sub?.Id == null)
                throw new ResponseNotUsable();
            Complete(claim, sub.Id.Value);
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for user {UserId} plan {Plan}", sub.Id, user.Id, handle);
            return (ToDto(sub, claim), true);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            var messages = ex.Error.Match(
                onErrorListResponse1: e => string.Join("; ", e.Errors),
                otherwise: _ => $"HTTP {(int)ex.StatusCode}");
            _logger.LogWarning("Maxio rejected subscription create for user {UserId} plan {Plan}: HTTP {Status}", user.Id, handle, (int)ex.StatusCode);
            if (IsDefiniteRefusal(ex))
                _db.MaxioSubscriptionClaims.Remove(claim);
            else
                claim.Status = ClaimStatus.Unknown;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw ex.StatusCode == HttpStatusCode.UnprocessableEntity
                ? new SubscriptionException(HttpStatusCode.UnprocessableEntity, "Maxio rejected the subscription: " + messages, ex)
                : Translate(ex, "create subscription");
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            // Outcome unknown: settle by re-reading with the reference we sent.
            var found = await TryFindSubscriptionAsync(reference, ct);
            if (found?.Id != null)
            {
                Complete(claim, found.Id.Value);
                await _db.SaveChangesAsync(CancellationToken.None);
                return (ToDto(found, claim), true);
            }
            claim.Status = ClaimStatus.Unknown;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogError(ex, "Subscription create outcome unknown for user {UserId} plan {Plan}", user.Id, handle);
            throw UnknownOutcome(ex);
        }
        catch (SdkException ex)
        {
            if (IsDefiniteRefusal(ex))
                _db.MaxioSubscriptionClaims.Remove(claim);
            else
                claim.Status = ClaimStatus.Unknown;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw Translate(ex, "create subscription");
        }
    }

    // ---------------------------------------------------------------- my subscriptions

    public async Task<(List<SubscriptionDto> Items, bool Truncated)> ListMySubscriptionsAsync(string userName, CancellationToken ct)
    {
        return await Bounded(ReadBudget, ct, async token =>
        {
            var user = await _users.FindByNameAsync(userName)
                ?? throw new SubscriptionException(HttpStatusCode.Unauthorized, "Unknown user.");
            var claims = await _db.MaxioSubscriptionClaims.Where(c => c.UserId == user.Id)
                .OrderBy(c => c.ClaimedAtUtc).Take(MySubscriptionsCap + 1).ToListAsync(token);
            var truncated = claims.Count > MySubscriptionsCap;
            var items = new List<SubscriptionDto>();
            foreach (var claim in claims.Take(MySubscriptionsCap))
                items.Add(await ReadExistingAsync(claim, token));
            return (items, truncated);
        });
    }

    /// <summary>Reads a claim's subscription from Maxio; degrades to the stored row (Available=false) instead of dropping it.</summary>
    private async Task<SubscriptionDto> ReadExistingAsync(MaxioSubscriptionClaim claim, CancellationToken ct)
    {
        try
        {
            Subscription? sub;
            if (claim.Status != ClaimStatus.Completed || !claim.MaxioSubscriptionId.HasValue)
            {
                // Unknown/stale claims are settled by the reference we sent.
                sub = await FindSubscriptionAsync(claim.Reference, ct);
                if (sub?.Id != null && claim.Status == ClaimStatus.Unknown)
                {
                    Complete(claim, sub.Id.Value);
                    await _db.SaveChangesAsync(CancellationToken.None);
                }
            }
            else
            {
                var response = await _client.Subscriptions.ReadSubscription(
                    new ReadSubscriptionRequest { SubscriptionId = claim.MaxioSubscriptionId.Value }, cancellationToken: ct);
                sub = response.Subscription;
            }
            // Ownership: the subscription we read must carry the reference we stored.
            if (sub != null && sub.Reference == claim.Reference)
                return ToDto(sub, claim);
        }
        catch (Exception ex) when (ex is SdkException or SubscriptionException)
        {
            _logger.LogWarning(ex, "Could not read subscription for plan {Plan}", claim.PlanHandle);
        }
        return new SubscriptionDto
        {
            SubscriptionId = claim.MaxioSubscriptionId, PlanHandle = claim.PlanHandle, Reference = claim.Reference,
            Available = false, ClaimStatus = claim.Status.ToString().ToLowerInvariant()
        };
    }

    // ---------------------------------------------------------------- provider reads

    private async Task<int?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var r = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return r.Customer?.Id;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex is not ResponseDeserializationException)
        {
            return null; // the provider's "no such customer"
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "read customer");
        }
    }

    private async Task<int?> TryFindCustomerAsync(string reference, CancellationToken ct)
    {
        try { return await FindCustomerAsync(reference, ct); }
        catch (SubscriptionException) { return null; } // still unknown; the caller records it
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            var r = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: ct);
            return r.Subscription;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex is not ResponseDeserializationException)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw Translate(ex, "find subscription");
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string reference, CancellationToken ct)
    {
        try { return await FindSubscriptionAsync(reference, ct); }
        catch (SubscriptionException) { return null; }
    }

    // ---------------------------------------------------------------- helpers

    private static void Complete(MaxioCustomerLink link, int customerId)
    {
        link.MaxioCustomerId = customerId;
        link.Status = ClaimStatus.Completed;
    }

    private static void Complete(MaxioSubscriptionClaim claim, int subscriptionId)
    {
        claim.MaxioSubscriptionId = subscriptionId;
        claim.Status = ClaimStatus.Completed;
    }

    /// <summary>The store refused the claim insert (SQL Server: DbUpdateException; in-memory provider: duplicate-key ArgumentException).</summary>
    private static bool IsClaimConflict(Exception ex) => ex is DbUpdateException or InvalidOperationException or ArgumentException;

    private static bool IsUnknownOutcome(Exception ex) =>
        ex is ResponseNotUsable or SdkConnectionException
        || ex is ResponseDeserializationException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices };

    /// <summary>The provider definitely did not act (a client-side refusal or our own credential problem).</summary>
    private static bool IsDefiniteRefusal(SdkException ex) =>
        ex is AuthSchemeException
        || (ex is ApiException a && (int)a.StatusCode is >= 400 and < 500 && (int)a.StatusCode != 429);

    private static SubscriptionException UnknownOutcome(Exception ex) => ex is SdkTimeoutException
        ? new SubscriptionException(HttpStatusCode.GatewayTimeout, "Billing did not answer in time; the outcome is unknown. Try again shortly.", ex)
        : new SubscriptionException(HttpStatusCode.BadGateway, "Billing outcome unknown. Try again shortly.", ex);

    private static SubscriptionDto ToDto(Subscription s, MaxioSubscriptionClaim claim) => new()
    {
        SubscriptionId = s.Id,
        PlanHandle = s.Product?.Handle ?? claim.PlanHandle,
        PlanName = s.Product?.Name,
        PriceInCents = s.ProductPriceInCents,
        Price = s.ProductPriceInCents.HasValue ? s.ProductPriceInCents.Value / 100m : null,
        State = s.State?.Value,
        NextBillingDate = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        Reference = claim.Reference,
        ClaimStatus = claim.Status.ToString().ToLowerInvariant()
    };

    /// <summary>Failure kind -> caller-facing status. Our credentials/quota are never the caller's fault.</summary>
    private SubscriptionException Translate(SdkException ex, string operation)
    {
        switch (ex)
        {
            case ResponseDeserializationException d:
                _logger.LogError(ex, "Maxio {Operation}: unreadable response (HTTP {Status}, {Target})", operation, (int)d.StatusCode, d.TargetType.Name);
                return new SubscriptionException(HttpStatusCode.BadGateway, "Billing returned a response that could not be processed.", ex);
            case ApiException a when a.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                _logger.LogError("Maxio {Operation}: HTTP {Status} (check Maxio credentials)", operation, (int)a.StatusCode);
                return new SubscriptionException(HttpStatusCode.BadGateway, "Billing is unavailable.", ex);
            case ApiException a when (int)a.StatusCode == 429:
                _logger.LogWarning("Maxio {Operation}: rate limited", operation);
                return new SubscriptionException(HttpStatusCode.ServiceUnavailable, "Billing is temporarily unavailable.", ex);
            case ApiException a:
                _logger.LogError("Maxio {Operation}: HTTP {Status}", operation, (int)a.StatusCode);
                return new SubscriptionException(HttpStatusCode.BadGateway, "Billing request failed.", ex);
            case SdkTimeoutException:
                _logger.LogError(ex, "Maxio {Operation}: timeout", operation);
                return new SubscriptionException(HttpStatusCode.GatewayTimeout, "Billing did not answer in time.", ex);
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio {Operation}: credentials not usable", operation);
                return new SubscriptionException(HttpStatusCode.BadGateway, "Billing is unavailable.", ex);
            default:
                _logger.LogError(ex, "Maxio {Operation}: connection failure", operation);
                return new SubscriptionException(HttpStatusCode.BadGateway, "Billing is unreachable.", ex);
        }
    }

    /// <summary>One total deadline for the whole handler, linked to the caller's cancellation.</summary>
    private static async Task<T> Bounded<T>(TimeSpan budget, CancellationToken ct, Func<CancellationToken, Task<T>> work)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        try
        {
            return await work(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SubscriptionException(HttpStatusCode.GatewayTimeout, "The request took too long.");
        }
    }

    /// <summary>Marker: a 2xx response lacked the member we need, so the write's outcome must be settled.</summary>
    private sealed class ResponseNotUsable : Exception { }
}
