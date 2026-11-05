using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.MaxioSubscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Sdk = MaxioAdvancedBilling;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record PlanDto(string Handle, string? Name, string? Description, long? PriceInCents, int? Interval, string? IntervalUnit);

public record PlanListDto(IReadOnlyList<PlanDto> Plans, bool IsPartial);

public record SubscriptionDto(int? SubscriptionId, string? ProductHandle, string? PlanName, long? PriceInCents,
    string? State, DateTimeOffset? NextBillingAt);

public record SubscribeResult(SubscriptionDto Subscription, bool Created);

/// <summary>
/// Subscribe flow. Order for every write: claim (own store, primary key) -> Maxio call -> record result.
/// </summary>
public class SubscriptionService
{
    // A Pending claim younger than this belongs to a request that is probably still running.
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(2);
    // Whole-call budget for one request, across every Maxio call it makes.
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private readonly MaxioGateway _maxio;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioGateway maxio, CatalogContext db, UserManager<ApplicationUser> users,
        ILogger<SubscriptionService> logger)
    {
        _maxio = maxio;
        _db = db;
        _users = users;
        _logger = logger;
    }

    public async Task<PlanListDto> ListPlansAsync(CancellationToken requestAborted)
    {
        using var cts = Bounded(requestAborted);
        var (products, partial) = await Guard(() => _maxio.ListPlansAsync(cts.Token));
        var plans = products.Where(p => !string.IsNullOrEmpty(p.Handle))
            .Select(p => new PlanDto(p.Handle!, p.Name, p.Description, p.PriceInCents, p.Interval, p.IntervalUnit?.Value))
            .ToList();
        return new PlanListDto(plans, partial);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken requestAborted)
    {
        var user = await RequireUserAsync(userName);
        var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == user.Id, requestAborted);
        if (link?.MaxioCustomerId is not int customerId)
            return Array.Empty<SubscriptionDto>();

        using var cts = Bounded(requestAborted);
        var subs = await Guard(() => _maxio.ListCustomerSubscriptionsAsync(customerId, cts.Token));
        return subs.Select(Map).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userName, string? productHandle, CancellationToken requestAborted)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new SubscriptionException(400, "productHandle is required.");

        var user = await RequireUserAsync(userName);
        using var cts = Bounded(requestAborted);   // one deadline shared by every Maxio call below
        var ct = cts.Token;

        // Cross-operation invariant: only a product the configured family offers can be subscribed to,
        // checked before any store or Maxio write.
        var (products, partial) = await Guard(() => _maxio.ListPlansAsync(ct));
        var product = products.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.Ordinal));
        if (product is null)
        {
            if (partial)   // cannot prove "not offered" from a capped list: fail closed
                throw new SubscriptionException(503, "Plan catalogue is temporarily unavailable.");
            throw new SubscriptionException(400, $"Unknown plan '{productHandle}'.");
        }

        var customerId = await EnsureCustomerAsync(user, ct);
        return await EnsureSubscriptionAsync(user, customerId, product.Handle!, ct);
    }

    // ---- customer -------------------------------------------------------------------------------

    private async Task<int> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct)
    {
        var reference = $"eshop-user-{user.Id}";
        var link = new MaxioCustomerLink
        {
            UserId = user.Id,
            Reference = reference,
            ClaimedAtUtc = DateTime.UtcNow,
            Status = MaxioClaimStatus.Pending
        };

        _db.MaxioCustomerLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(ct);                       // claim: primary key refuses a second caller
        }
        catch (Exception ex) when (IsClaimConflict(ex))
        {
            _db.Entry(link).State = EntityState.Detached;
            var existing = await _db.MaxioCustomerLinks.FirstAsync(l => l.UserId == user.Id, ct);
            if (existing.Status == MaxioClaimStatus.Ready && existing.MaxioCustomerId is int readyId)
                return readyId;
            if (DateTime.UtcNow - existing.ClaimedAtUtc < ClaimTtl)
                throw new SubscriptionException(409, "A subscription request for this account is already in progress.");

            // Stale Pending claim: settle by reference before ever creating again.
            _logger.LogWarning("Reclaiming stale Maxio customer claim for user {UserId}", user.Id);
            var found = await Guard(() => _maxio.FindCustomerIdByReferenceAsync(reference, ct));
            if (found is int foundId)
            {
                await MarkCustomerReadyAsync(existing, foundId, ct);
                return foundId;
            }
            existing.ClaimedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            link = existing;
        }

        try
        {
            var id = await _maxio.CreateCustomerAsync(FirstName(user), "Shopper", user.Email ?? user.UserName!, reference, ct);
            await MarkCustomerReadyAsync(link, id, ct);
            return id;
        }
        catch (MaxioOutcomeUnknownException ex)
        {
            // The create may have landed: re-read by reference before reporting anything.
            _logger.LogError(ex, "Maxio customer create outcome unknown for user {UserId}; reconciling", user.Id);
            int? found = null;
            try { found = await _maxio.FindCustomerIdByReferenceAsync(reference, CancellationToken.None); }
            catch (Exception readEx) when (readEx is MaxioUnavailableException or MaxioRejectedException) { }
            if (found is int foundId)
            {
                await MarkCustomerReadyAsync(link, foundId, ct);
                return foundId;
            }
            // Still unknown: leave the claim Pending; the next request settles it by the same re-read.
            throw new SubscriptionException(409, "Your sign-up is being processed; please retry shortly.", ex);
        }
        catch (MaxioRejectedException)
        {
            _db.MaxioCustomerLinks.Remove(link);                  // release: Maxio refused, nothing exists
            await _db.SaveChangesAsync(CancellationToken.None);
            throw Wrap(new MaxioRejectedException("The billing system rejected the customer details."));
        }
        catch (MaxioUnavailableException ex)
        {
            _db.MaxioCustomerLinks.Remove(link);                  // release: the call was refused, not ambiguous
            await _db.SaveChangesAsync(CancellationToken.None);
            throw Wrap(ex);
        }
    }

    private async Task MarkCustomerReadyAsync(MaxioCustomerLink link, int customerId, CancellationToken ct)
    {
        link.MaxioCustomerId = customerId;
        link.Status = MaxioClaimStatus.Ready;
        await _db.SaveChangesAsync(ct);
    }

    // ---- subscription ---------------------------------------------------------------------------

    private async Task<SubscribeResult> EnsureSubscriptionAsync(ApplicationUser user, int customerId, string handle, CancellationToken ct)
    {
        var reference = $"eshop-sub-{user.Id}-{handle}";
        var claim = new MaxioSubscriptionClaim
        {
            UserId = user.Id,
            ProductHandle = handle,
            Reference = reference,
            ClaimedAtUtc = DateTime.UtcNow,
            Status = MaxioClaimStatus.Pending
        };

        _db.MaxioSubscriptionClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);                       // claim: composite primary key
        }
        catch (Exception ex) when (IsClaimConflict(ex))
        {
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.MaxioSubscriptionClaims.FirstAsync(c => c.UserId == user.Id && c.ProductHandle == handle, ct);
            if (existing.Status == MaxioClaimStatus.Ready)
            {
                var current = await Guard(() => _maxio.FindSubscriptionByReferenceAsync(reference, ct));
                if (current is not null)
                    return new SubscribeResult(Map(current), false);   // already subscribed: same answer, no write
            }
            else if (DateTime.UtcNow - existing.ClaimedAtUtc < ClaimTtl)
            {
                throw new SubscriptionException(409, "A subscription request for this plan is already in progress.");
            }

            _logger.LogWarning("Reclaiming stale Maxio subscription claim for user {UserId}, plan {Plan}", user.Id, handle);
            var found = await Guard(() => _maxio.FindSubscriptionByReferenceAsync(reference, ct));
            if (found is not null)
            {
                await MarkSubscriptionReadyAsync(existing, found.Id, ct);
                return new SubscribeResult(Map(found), false);
            }
            existing.Status = MaxioClaimStatus.Pending;
            existing.ClaimedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            claim = existing;
        }

        try
        {
            var sub = await _maxio.CreateSubscriptionAsync(customerId, handle, reference, ct);
            await MarkSubscriptionReadyAsync(claim, sub.Id, ct);
            return new SubscribeResult(Map(sub), true);
        }
        catch (MaxioOutcomeUnknownException ex)
        {
            _logger.LogError(ex, "Maxio subscription create outcome unknown for user {UserId}; reconciling", user.Id);
            Sdk.Models.Subscription? found = null;
            try { found = await _maxio.FindSubscriptionByReferenceAsync(reference, CancellationToken.None); }
            catch (Exception readEx) when (readEx is MaxioUnavailableException or MaxioRejectedException) { }
            if (found is not null)
            {
                await MarkSubscriptionReadyAsync(claim, found.Id, ct);
                return new SubscribeResult(Map(found), true);
            }
            throw new SubscriptionException(409, "Your subscription is being processed; please retry shortly.", ex);
        }
        catch (Exception ex) when (ex is MaxioRejectedException or MaxioUnavailableException)
        {
            _db.MaxioSubscriptionClaims.Remove(claim);            // release: Maxio refused the call
            await _db.SaveChangesAsync(CancellationToken.None);
            throw Wrap(ex);
        }
    }

    private async Task MarkSubscriptionReadyAsync(MaxioSubscriptionClaim claim, int? subscriptionId, CancellationToken ct)
    {
        claim.MaxioSubscriptionId = subscriptionId;
        claim.Status = MaxioClaimStatus.Ready;
        await _db.SaveChangesAsync(ct);
    }

    // A second claim for the same key: DbUpdateException on relational stores; the in-memory provider
    // surfaces the same duplicate-key refusal as a bare ArgumentException from SaveChanges.
    private static bool IsClaimConflict(Exception ex) => ex is DbUpdateException or ArgumentException;

    // ---- helpers --------------------------------------------------------------------------------

    private static SubscriptionDto Map(Sdk.Models.Subscription s) => new(
        s.Id,
        s.Product?.Handle,
        s.Product?.Name,
        s.Product?.PriceInCents ?? s.CurrentBillingAmountInCents,
        s.State?.Value,
        s.NextAssessmentAt ?? s.CurrentPeriodEndsAt);

    private async Task<ApplicationUser> RequireUserAsync(string userName)
    {
        var user = await _users.FindByNameAsync(userName);
        return user ?? throw new SubscriptionException(401, "Unknown user.");
    }

    private static string FirstName(ApplicationUser user) => user.UserName ?? "eShop";

    private static CancellationTokenSource Bounded(CancellationToken requestAborted)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(Budget);
        return cts;
    }

    private static SubscriptionException Wrap(Exception ex) => ex switch
    {
        MaxioRejectedException r => new SubscriptionException(422, r.Message, r),
        _ => new SubscriptionException(502, "The billing system is temporarily unavailable.", ex)
    };

    private async Task<T> Guard<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (MaxioRejectedException ex) { throw Wrap(ex); }
        catch (MaxioUnavailableException ex) { throw Wrap(ex); }
        catch (MaxioOutcomeUnknownException ex) { throw Wrap(ex); }
        catch (OperationCanceledException ex) when (ex is not null)
        {
            throw new SubscriptionException(504, "The billing system did not answer in time.", ex);
        }
    }
}
