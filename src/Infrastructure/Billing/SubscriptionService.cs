using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Subscribe flow. Every write follows: claim (insert, refused by the primary key for a second caller)
/// -> Maxio call -> record the result. A claim is released when Maxio definitively refuses and kept
/// (as an unknown outcome) when the write may have landed; later calls settle it by re-reading Maxio.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CustomerPollInterval = TimeSpan.FromMilliseconds(500);
    private const int CUSTOMER_POLL_ATTEMPTS = 10;

    private readonly CatalogContext _db;
    private readonly IMaxioGateway _gateway;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly TimeProvider _time;

    public SubscriptionService(CatalogContext db, IMaxioGateway gateway, ILogger<SubscriptionService> logger, TimeProvider? time = null)
    {
        _db = db;
        _gateway = gateway;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken)
    {
        var listing = await Gateway(() => _gateway.ListPlansAsync(cancellationToken));
        return new SubscriptionPlanList(listing.Plans, listing.IsComplete);
    }

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken)
    {
        var buyerId = BuyerId(subscriber.UserName);

        // Invariant: only a plan the family currently offers can be subscribed to.
        var listing = await Gateway(() => _gateway.ListPlansAsync(cancellationToken));
        var plan = listing.Plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionException(SubscriptionFailure.PlanNotFound, $"Plan '{planHandle}' is not available.");

        var reference = Reference("eshop-sub", buyerId, plan.Handle);
        var enrollment = new SubscriptionEnrollment(buyerId, plan.Handle, reference, _time.GetUtcNow());

        // CLAIM: the composite primary key refuses a second claim for the same user and plan.
        _db.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsDuplicateClaim(ex))
        {
            _db.ChangeTracker.Clear();
            return await ResolveExistingAsync(buyerId, plan.Handle, reference, cancellationToken);
        }

        var keepClaim = false; // true only while a subscription write of unknown outcome is pending
        try
        {
            var customerId = await EnsureCustomerAsync(buyerId, subscriber, cancellationToken);

            SubscriptionSummary created;
            try
            {
                // SDK CALL (a POST: never resent by the SDK)
                created = await _gateway.CreateSubscriptionAsync(customerId, plan.Handle, reference, cancellationToken);
            }
            catch (MaxioGatewayException ex) when (ex.Kind == MaxioFailureKind.UnknownOutcome)
            {
                // The write may have landed: settle by re-reading Maxio with the reference we sent.
                var found = await SettleSubscriptionAsync(reference, cancellationToken);
                if (found is null)
                {
                    _logger.LogError(ex, "Subscription outcome for {Reference} is unknown; claim kept as pending", reference);
                    keepClaim = true;
                    throw new SubscriptionException(SubscriptionFailure.InProgress,
                        "The subscription is being processed. Check your subscriptions shortly.", ex);
                }
                created = found;
            }

            // RECORD
            enrollment.Activate(created.SubscriptionId);
            await _db.SaveChangesAsync(cancellationToken);
            return new SubscribeResult(created, AlreadySubscribed: false);
        }
        catch (MaxioGatewayException ex)
        {
            await ReleaseEnrollmentAsync(buyerId, plan.Handle);
            throw Translate(ex);
        }
        catch (SubscriptionException) when (!keepClaim)
        {
            await ReleaseEnrollmentAsync(buyerId, plan.Handle);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var buyerId = BuyerId(userName);
        var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.BuyerId == buyerId, cancellationToken);
        if (link is null) return Array.Empty<SubscriptionSummary>();

        var customerId = link.MaxioCustomerId
            ?? await Gateway(() => _gateway.FindCustomerIdByReferenceAsync(link.Reference, cancellationToken));
        if (customerId is null) return Array.Empty<SubscriptionSummary>();

        // Settle any enrollment whose creation outcome was unknown.
        var pending = await _db.SubscriptionEnrollments
            .Where(e => e.BuyerId == buyerId && e.Status == EnrollmentStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (var e in pending)
        {
            var found = await Gateway(() => _gateway.FindSubscriptionByReferenceAsync(e.Reference, cancellationToken));
            if (found is not null) e.Activate(found.SubscriptionId);
            else if (_time.GetUtcNow() - e.CreatedAt > StaleClaimAge) _db.SubscriptionEnrollments.Remove(e);
        }
        if (pending.Count > 0) await _db.SaveChangesAsync(cancellationToken);

        return await Gateway(() => _gateway.ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken));
    }

    // A claim already exists for this user+plan.
    private async Task<SubscribeResult> ResolveExistingAsync(string buyerId, string planHandle, string reference, CancellationToken ct)
    {
        var existing = await _db.SubscriptionEnrollments
            .FirstOrDefaultAsync(e => e.BuyerId == buyerId && e.PlanHandle == planHandle, ct);
        if (existing is null) // released between our failed claim and this read
            throw new SubscriptionException(SubscriptionFailure.InProgress, "Please retry the request.");

        var found = await Gateway(() => _gateway.FindSubscriptionByReferenceAsync(reference, ct));
        if (found is not null)
        {
            if (existing.Status == EnrollmentStatus.Pending)
            {
                existing.Activate(found.SubscriptionId);
                await _db.SaveChangesAsync(ct);
            }
            return new SubscribeResult(found, AlreadySubscribed: true);
        }

        if (existing.Status == EnrollmentStatus.Pending && _time.GetUtcNow() - existing.CreatedAt > StaleClaimAge)
        {
            // Abandoned claim and Maxio holds nothing for it: release so the next attempt can proceed.
            _db.SubscriptionEnrollments.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
        throw new SubscriptionException(SubscriptionFailure.InProgress, "A subscription request for this plan is already being processed.");
    }

    private async Task<SubscriptionSummary?> SettleSubscriptionAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await _gateway.FindSubscriptionByReferenceAsync(reference, ct);
        }
        catch (MaxioGatewayException ex)
        {
            _logger.LogWarning(ex, "Could not re-read subscription {Reference} to settle an unknown outcome", reference);
            return null;
        }
    }

    private async Task<int> EnsureCustomerAsync(string buyerId, Subscriber subscriber, CancellationToken ct, bool retried = false)
    {
        var customerRef = Reference("eshop-cust", buyerId);

        // Fast path only: the claim below, not this read, is what refuses a duplicate.
        var known = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.BuyerId == buyerId, ct);
        if (known?.MaxioCustomerId is { } knownId) return knownId;

        var link = new MaxioCustomerLink(buyerId, customerRef, _time.GetUtcNow());

        // CLAIM: the primary key (BuyerId) refuses a second customer claim for this user.
        _db.MaxioCustomerLinks.Add(link);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsDuplicateClaim(ex))
        {
            _db.ChangeTracker.Clear();
            return await AwaitCustomerAsync(buyerId, subscriber, ct, retried);
        }

        try
        {
            var id = await _gateway.FindCustomerIdByReferenceAsync(customerRef, ct);
            if (id is null)
            {
                try
                {
                    // SDK CALL
                    id = await _gateway.CreateCustomerAsync(customerRef, FirstName(subscriber), "Customer", subscriber.Email, ct);
                }
                catch (MaxioGatewayException ex) when (ex.Kind == MaxioFailureKind.UnknownOutcome)
                {
                    id = await _gateway.FindCustomerIdByReferenceAsync(customerRef, ct); // settle
                    if (id is null)
                    {
                        _logger.LogError(ex, "Customer outcome for {Reference} is unknown; claim kept", customerRef);
                        throw new SubscriptionException(SubscriptionFailure.InProgress,
                            "Your billing account is being set up. Please retry shortly.", ex);
                    }
                }
            }

            // RECORD
            link.Resolve(id.Value);
            await _db.SaveChangesAsync(ct);
            return id.Value;
        }
        catch (MaxioGatewayException ex)
        {
            await ReleaseCustomerLinkAsync(buyerId);
            throw Translate(ex);
        }
    }

    // Another request holds the customer claim: wait for it to publish the Maxio customer id.
    private async Task<int> AwaitCustomerAsync(string buyerId, Subscriber subscriber, CancellationToken ct, bool retried)
    {
        for (var i = 0; i < CUSTOMER_POLL_ATTEMPTS; i++)
        {
            var link = await _db.MaxioCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.BuyerId == buyerId, ct);
            if (link is null) // the claimant failed and released it
            {
                if (retried) break;
                return await EnsureCustomerAsync(buyerId, subscriber, ct, retried: true);
            }
            if (link.MaxioCustomerId is { } id) return id;

            if (_time.GetUtcNow() - link.CreatedAt > StaleClaimAge)
            {
                var found = await Gateway(() => _gateway.FindCustomerIdByReferenceAsync(link.Reference, ct));
                if (found is { } foundId)
                {
                    var tracked = await _db.MaxioCustomerLinks.FirstAsync(l => l.BuyerId == buyerId, ct);
                    tracked.Resolve(foundId);
                    await _db.SaveChangesAsync(ct);
                    return foundId;
                }
                await ReleaseCustomerLinkAsync(buyerId);
                break;
            }
            await Task.Delay(CustomerPollInterval, ct);
        }
        throw new SubscriptionException(SubscriptionFailure.InProgress, "Your billing account is being set up. Please retry shortly.");
    }

    private async Task ReleaseEnrollmentAsync(string buyerId, string planHandle)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.SubscriptionEnrollments.FirstOrDefaultAsync(e => e.BuyerId == buyerId && e.PlanHandle == planHandle);
        if (row is not null && row.Status == EnrollmentStatus.Pending)
        {
            _db.SubscriptionEnrollments.Remove(row);
            await _db.SaveChangesAsync();
        }
    }

    private async Task ReleaseCustomerLinkAsync(string buyerId)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.MaxioCustomerLinks.FirstOrDefaultAsync(l => l.BuyerId == buyerId);
        if (row is not null && row.MaxioCustomerId is null)
        {
            _db.MaxioCustomerLinks.Remove(row);
            await _db.SaveChangesAsync();
        }
    }

    private static async Task<T> Gateway<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (MaxioGatewayException ex)
        {
            throw Translate(ex);
        }
    }

    private static SubscriptionException Translate(MaxioGatewayException ex) => ex.Kind switch
    {
        MaxioFailureKind.Rejected => new SubscriptionException(SubscriptionFailure.Rejected, ex.Message, ex),
        _ => new SubscriptionException(SubscriptionFailure.ProviderUnavailable, "Billing is temporarily unavailable.", ex)
    };

    // SQL providers raise DbUpdateException for a key violation; the in-memory provider raises ArgumentException.
    private static bool IsDuplicateClaim(Exception ex) => ex is DbUpdateException or ArgumentException;

    private static string BuyerId(string userName) => userName.Trim().ToLowerInvariant();

    private static string FirstName(Subscriber subscriber)
    {
        var local = subscriber.Email.Split('@')[0];
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }

    private static string Reference(string prefix, params string[] parts)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)));
        return prefix + "-" + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }
}
