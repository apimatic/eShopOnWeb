using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Cancelling (with a full refund) and shipping orders paid by bank.</summary>
public class BankOrderService
{
    private readonly TransferReverser _reverser;
    private readonly CatalogContext _db;

    public BankOrderService(TransferReverser reverser, CatalogContext db)
    {
        _reverser = reverser;
        _db = db;
    }

    public async Task<IResult> CancelAsync(string userName, int orderId, CancellationToken ct)
    {
        var owner = ApiResults.OwnerKey(userName);
        using var _ = await CheckoutService.LockAsync("order:" + orderId, ct);
        var order = await _db.BankOrders.Include(o => o.Transfers)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.OwnerKey == owner, ct);
        if (order is null) return ApiResults.NotFound("Order not found.");
        if (order.Status == BankOrderStatuses.Cancelled) return Results.Ok(CancelDto(order));
        if (order.Status == BankOrderStatuses.Shipped) return ApiResults.Conflict("The order has already shipped and can no longer be cancelled.");

        try
        {
            // Every transfer that paid the order, so the whole amount charged (items, shipping, fee) comes back.
            foreach (var transfer in order.Transfers.Where(t => !t.Reversed))
            {
                transfer.RefundId = await _reverser.ReverseAsync(transfer.TransferId, transfer.Amount, $"refund-order-{order.Id}-{transfer.Id}", ct);
                transfer.Reversed = true;
                await _db.SaveChangesAsync(ct); // progress survives a failure on a later transfer
            }
        }
        catch (PlaidException ex)
        {
            return ApiResults.FromPlaid(ex);
        }

        order.Status = BankOrderStatuses.Cancelled;
        order.CancelledAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Results.Ok(CancelDto(order));
    }

    public async Task<IResult> ShipAsync(int orderId, CancellationToken ct)
    {
        using var _ = await CheckoutService.LockAsync("order:" + orderId, ct);
        var order = await _db.BankOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return ApiResults.NotFound("Order not found.");
        if (order.Status == BankOrderStatuses.Cancelled) return ApiResults.Conflict("The order was cancelled.");
        if (order.Status == BankOrderStatuses.Paid)
        {
            order.Status = BankOrderStatuses.Shipped;
            order.ShippedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return Results.Ok(new { orderId = order.Id, status = order.Status, shippedAt = order.ShippedAt });
    }

    private static object CancelDto(BankOrder o) => new
    {
        orderId = o.Id,
        status = o.Status,
        refunded = o.Total,
        refundIds = o.Transfers.Where(t => t.RefundId != null).Select(t => t.RefundId)
    };
}
