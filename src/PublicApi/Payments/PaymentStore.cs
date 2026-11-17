using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The application-owned persistence for payments. Claim inserts run on a fresh context so a
/// duplicate primary key cannot poison the request's scoped unit of work.
/// </summary>
public sealed class PaymentStore
{
    private readonly CatalogContext _db;
    private readonly DbContextOptions<CatalogContext> _options;

    public PaymentStore(CatalogContext db, DbContextOptions<CatalogContext> options)
    {
        _db = db;
        _options = options;
    }

    public Task<Order?> GetOrderWithItemsAsync(int orderId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public Task<List<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

    public Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public async Task AddOrderPaymentAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    public Task<CatalogItemSnapshot[]> GetCatalogItemsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken) =>
        _db.CatalogItems
            .Where(i => ids.Contains(i.Id))
            .Select(i => new CatalogItemSnapshot(i.Id, i.Name, i.PictureUri, i.Price))
            .ToArrayAsync(cancellationToken);

    public async Task<Order> AddOrderAsync(Order order, CancellationToken cancellationToken)
    {
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public Task<ShopperPaymentIdentity?> GetIdentityAsync(string buyerId, CancellationToken cancellationToken) =>
        _db.ShopperPaymentIdentities.FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);

    public async Task<bool> TryClaimIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        fresh.ShopperPaymentIdentities.Add(new ShopperPaymentIdentity { BuyerId = buyerId });
        return await TrySaveClaimAsync(fresh, cancellationToken);
    }

    public async Task CompleteIdentityAsync(string buyerId, string finixIdentityId, CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var claim = await fresh.ShopperPaymentIdentities.FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
        if (claim is null)
        {
            fresh.ShopperPaymentIdentities.Add(new ShopperPaymentIdentity
            {
                BuyerId = buyerId,
                FinixIdentityId = finixIdentityId
            });
        }
        else
        {
            claim.FinixIdentityId = finixIdentityId;
        }
        await fresh.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseIdentityClaimAsync(string buyerId, CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var claim = await fresh.ShopperPaymentIdentities.FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
        if (claim is not null && string.IsNullOrWhiteSpace(claim.FinixIdentityId))
        {
            fresh.ShopperPaymentIdentities.Remove(claim);
            await fresh.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<SavedCardClaim?> GetSavedCardClaimAsync(
        string buyerId,
        string fingerprint,
        CancellationToken cancellationToken) =>
        _db.SavedCardClaims.FirstOrDefaultAsync(
            c => c.BuyerId == buyerId && c.CardFingerprint == fingerprint,
            cancellationToken);

    public async Task<bool> TryClaimSavedCardAsync(
        string buyerId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        fresh.SavedCardClaims.Add(new SavedCardClaim { BuyerId = buyerId, CardFingerprint = fingerprint });
        return await TrySaveClaimAsync(fresh, cancellationToken);
    }

    public async Task<SavedPaymentMethod?> CompleteSavedCardAsync(
        SavedPaymentMethod method,
        string buyerId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var claim = await fresh.SavedCardClaims.FirstOrDefaultAsync(
            c => c.BuyerId == buyerId && c.CardFingerprint == fingerprint,
            cancellationToken);

        var existing = await fresh.SavedPaymentMethods.FirstOrDefaultAsync(
            m => m.BuyerId == buyerId && m.CardFingerprint == fingerprint,
            cancellationToken);

        if (existing is not null)
        {
            existing.FinixPaymentInstrumentId = method.FinixPaymentInstrumentId;
            existing.Brand = method.Brand;
            existing.LastFour = method.LastFour;
            existing.ExpirationMonth = method.ExpirationMonth;
            existing.ExpirationYear = method.ExpirationYear;
            existing.Enabled = true;
            existing.CreatedAt = method.CreatedAt;
            if (claim is not null)
                claim.PaymentMethodId = existing.Id;

            await fresh.SaveChangesAsync(cancellationToken);
            return existing;
        }

        fresh.SavedPaymentMethods.Add(method);
        if (claim is not null)
            claim.PaymentMethodId = method.Id;

        await fresh.SaveChangesAsync(cancellationToken);
        return method;
    }

    public async Task RemoveSavedCardClaimAsync(
        string buyerId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var claim = await fresh.SavedCardClaims.FirstOrDefaultAsync(
            c => c.BuyerId == buyerId && c.CardFingerprint == fingerprint,
            cancellationToken);
        if (claim is not null)
        {
            fresh.SavedCardClaims.Remove(claim);
            await fresh.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ReleaseSavedCardClaimAsync(
        string buyerId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var claim = await fresh.SavedCardClaims.FirstOrDefaultAsync(
            c => c.BuyerId == buyerId && c.CardFingerprint == fingerprint,
            cancellationToken);
        if (claim is not null && string.IsNullOrWhiteSpace(claim.PaymentMethodId))
        {
            fresh.SavedCardClaims.Remove(claim);
            await fresh.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<SavedPaymentMethod?> GetSavedMethodAsync(
        string buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken) =>
        _db.SavedPaymentMethods.FirstOrDefaultAsync(
            m => m.BuyerId == buyerId && m.Id == paymentMethodId,
            cancellationToken);

    public Task<List<SavedPaymentMethod>> GetSavedMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken) =>
        _db.SavedPaymentMethods
            .Where(m => m.BuyerId == buyerId && m.Enabled)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<OrderRefund?> GetRefundAsync(
        int orderId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _db.OrderRefunds.FirstOrDefaultAsync(
            r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public Task<List<OrderRefund>> GetRefundsAsync(int orderId, CancellationToken cancellationToken) =>
        _db.OrderRefunds.Where(r => r.OrderId == orderId).ToListAsync(cancellationToken);

    public async Task<bool> TryClaimRefundAsync(OrderRefund refund, CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        fresh.OrderRefunds.Add(refund);
        return await TrySaveClaimAsync(fresh, cancellationToken);
    }

    public async Task CompleteRefundAsync(
        int orderId,
        string idempotencyKey,
        string finixReversalId,
        string? state,
        long totalRefundedCents,
        PaymentStatus status,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var refund = await fresh.OrderRefunds.FirstOrDefaultAsync(
            r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (refund is not null)
        {
            refund.FinixReversalId = finixReversalId;
            refund.State = state;
        }

        var payment = await fresh.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
        if (payment is not null)
        {
            payment.RefundedAmountCents = totalRefundedCents;
            payment.Status = status;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await fresh.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkRefundUnknownAsync(
        int orderId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var refund = await fresh.OrderRefunds.FirstOrDefaultAsync(
            r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (refund is not null)
        {
            refund.State = "UNKNOWN";
            await fresh.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ReleaseRefundClaimAsync(
        int orderId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var refund = await fresh.OrderRefunds.FirstOrDefaultAsync(
            r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (refund is not null && string.IsNullOrWhiteSpace(refund.FinixReversalId))
        {
            fresh.OrderRefunds.Remove(refund);
            await fresh.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> TryClaimOperationAsync(
        int orderId,
        string operation,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        fresh.PaymentClaims.Add(new PaymentClaim { OrderId = orderId, Operation = operation });
        return await TrySaveClaimAsync(fresh, cancellationToken);
    }

    public async Task ReleaseOperationClaimAsync(
        int orderId,
        string operation,
        CancellationToken cancellationToken)
    {
        await using var fresh = new CatalogContext(_options);
        var claim = await fresh.PaymentClaims.FirstOrDefaultAsync(
            c => c.OrderId == orderId && c.Operation == operation,
            cancellationToken);
        if (claim is not null)
        {
            fresh.PaymentClaims.Remove(claim);
            await fresh.SaveChangesAsync(cancellationToken);
        }
    }

    public Task<List<OrderPayment>> GetPaymentsInRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        _db.OrderPayments
            .Where(p => p.UpdatedAt >= from && p.UpdatedAt <= to)
            .ToListAsync(cancellationToken);

    public Task<List<OrderRefund>> GetRefundsInRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        _db.OrderRefunds
            .Where(r => r.CreatedAt >= from && r.CreatedAt <= to)
            .ToListAsync(cancellationToken);

    private static async Task<bool> TrySaveClaimAsync(
        CatalogContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // The EF InMemory store surfaces a duplicate primary key as ArgumentException.
            return false;
        }
    }
}

public sealed record CatalogItemSnapshot(int Id, string Name, string PictureUri, decimal Price);
