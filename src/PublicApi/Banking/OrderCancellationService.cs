using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record CancellationResult(int OrderId, string Status, decimal Refunded, string Method);

/// <summary>Cancelling an order paid by bank: the shopper gets everything back, the fee included.</summary>
public class OrderCancellationService
{
    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly KeyedLock _locks;

    public OrderCancellationService(ThePlaidApiClient plaid, CatalogContext db, KeyedLock locks)
    {
        _plaid = plaid;
        _db = db;
        _locks = locks;
    }

    public async Task<CancellationResult> CancelAsync(string shopperId, int orderId, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync($"order:{orderId}", ct);

        var order = await _db.BankOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.OwnerId != shopperId)
            throw BankingException.NotFound("No such order.");
        if (order.Status == BankOrderStatus.Cancelled)
            throw BankingException.Conflict("That order is already cancelled.");
        if (order.ShippedOn is not null)
            throw BankingException.Conflict("That order has already shipped and can no longer be cancelled.");

        var transfer = (await PlaidCalls.Run(() => _plaid.TransferGet(new TransferGetOperationRequest
        {
            Body = new TransferGetRequest { TransferId = order.TransferId }
        }, cancellationToken: ct))).Transfer;

        string method;
        decimal refunded;
        if (transfer.Status == TransferStatus.Cancelled || transfer.Status == TransferStatus.Failed
            || transfer.Status == TransferStatus.Returned)
        {
            // The money never moved (or came back by itself).
            method = "none";
            refunded = 0m;
        }
        else if (transfer.Cancellable)
        {
            // Not sent to the bank yet: stop it, so the money never leaves the account.
            await PlaidCalls.Run(() => _plaid.TransferCancel(new TransferCancelOperationRequest
            {
                Body = new TransferCancelRequest { TransferId = order.TransferId }
            }, cancellationToken: ct));
            method = "cancelled";
            refunded = order.Charged;
        }
        else
        {
            // Already on its way: send back what was charged, fee included, minus anything refunded already.
            var alreadyRefunded = transfer.Refunds
                .Where(r => r.Status != TransferRefundStatus.Failed && r.Status != TransferRefundStatus.Cancelled)
                .Sum(r => Money.FromPlaid(r.Amount));
            var remaining = order.Charged - alreadyRefunded;
            if (remaining > 0)
            {
                var refund = (await PlaidCalls.Run(() => _plaid.TransferRefundCreate(new TransferRefundCreateOperationRequest
                {
                    Body = new TransferRefundCreateRequest
                    {
                        TransferId = order.TransferId,
                        Amount = Money.ToPlaid(remaining),
                        IdempotencyKey = $"refund-{order.Id}-{order.TransferId[..8]}",
                    }
                }, cancellationToken: ct))).Refund;
                order.RefundId = refund.Id;
            }
            method = "refunded";
            refunded = order.Charged;
        }

        order.Status = BankOrderStatus.Cancelled;
        order.CancelledAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new CancellationResult(order.Id, order.Status, refunded, method);
    }
}
