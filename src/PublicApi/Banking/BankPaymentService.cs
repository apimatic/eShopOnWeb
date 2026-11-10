using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record OrderCancellationDto(int OrderId, string Status, decimal Refunded, DateTimeOffset? CancelledAt);

public sealed record OrderShippedDto(int OrderId, DateTimeOffset ShippedAt);

/// <summary>Cancelling (with a full refund) and shipping orders that were paid by bank.</summary>
public class BankPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    private readonly PlaidApi _plaid;
    private readonly CatalogContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<BankPaymentService> _logger;

    public BankPaymentService(PlaidApi plaid, CatalogContext db, TimeProvider time, ILogger<BankPaymentService> logger)
    {
        _plaid = plaid;
        _db = db;
        _time = time;
        _logger = logger;
    }

    public async Task<OrderCancellationDto> CancelAsync(string userName, int orderId)
    {
        var gate = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var payment = await _db.BankPayments.Include(p => p.Transfers)
                .FirstOrDefaultAsync(p => p.OrderId == orderId && p.UserName == userName)
                ?? throw new ApiRequestException(HttpStatusCode.NotFound, "The order was not found.");

            var refunded = payment.Total + payment.Fee;
            if (payment.Status == BankPaymentStatus.Cancelled)
            {
                return new OrderCancellationDto(orderId, "cancelled", refunded, payment.CancelledAt);
            }

            if (payment.ShippedAt is not null)
            {
                throw new ApiRequestException(HttpStatusCode.Conflict, "The order already shipped and can't be cancelled.");
            }

            // Every transfer is brought to "no money taken": cancelled while it can still be, refunded after.
            // Each step looks at Plaid's records first, so a cancellation that stopped half way can be repeated.
            foreach (var transfer in payment.Transfers)
            {
                await ReturnTransferAsync(transfer);
            }

            payment.Status = BankPaymentStatus.Cancelled;
            payment.CancelledAt = _time.GetUtcNow();
            payment.Version++;
            await _db.SaveChangesAsync();
            return new OrderCancellationDto(orderId, "cancelled", refunded, payment.CancelledAt);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ReturnTransferAsync(BankPaymentTransfer owned)
    {
        var transfer = await GetTransferAsync(owned.PlaidTransferId);
        var status = transfer.Status.Value;
        if (status is "cancelled" or "failed")
        {
            return; // the money was never taken
        }

        if (transfer.Cancellable)
        {
            try
            {
                await _plaid.CallAsync(c => c.TransferCancel(new TransferCancelOperationRequest
                {
                    Body = new TransferCancelRequest { TransferId = owned.PlaidTransferId }
                }));
                return;
            }
            catch (ApiRequestException ex) when (PlaidApi.CodeOf(ex) == "TRANSFER_NOT_CANCELLABLE")
            {
                // It moved on in the meantime: refund it instead.
                transfer = await GetTransferAsync(owned.PlaidTransferId);
            }
        }

        if (transfer.Status.Value is "cancelled" or "failed")
        {
            return;
        }

        var amount = decimal.Parse(transfer.Amount, NumberStyles.Number, CultureInfo.InvariantCulture);
        var alreadyRefunded = transfer.Refunds
            .Where(r => r.Status.Value is not ("failed" or "cancelled"))
            .Sum(r => decimal.Parse(r.Amount, NumberStyles.Number, CultureInfo.InvariantCulture));
        var remaining = amount - alreadyRefunded;
        if (remaining <= 0m)
        {
            return;
        }

        var attempt = transfer.Refunds.Count + 1;
        try
        {
            await _plaid.CallAsync(c => c.TransferRefundCreate(new TransferRefundCreateOperationRequest
            {
                Body = new TransferRefundCreateRequest
                {
                    TransferId = owned.PlaidTransferId,
                    Amount = BankCheckoutService.FormatAmount(remaining),
                    IdempotencyKey = $"r{attempt}-{owned.PlaidTransferId}"
                }
            }));
        }
        catch (ApiRequestException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning(ex, "Refund of transfer {TransferId} was refused", owned.PlaidTransferId);
            throw new ApiRequestException(HttpStatusCode.Conflict,
                "The payment can't be returned right now: " + ex.Message, ex);
        }
    }

    private async Task<Transfer> GetTransferAsync(string transferId) =>
        (await _plaid.CallAsync(c => c.TransferGet(new TransferGetOperationRequest
        {
            Body = new TransferGetRequest { TransferId = transferId }
        }))).Transfer;

    /// <summary>Marks a paid order as shipped: from then on it can't be cancelled.</summary>
    public async Task<OrderShippedDto> MarkShippedAsync(int orderId)
    {
        var gate = Locks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var payment = await _db.BankPayments.FirstOrDefaultAsync(p => p.OrderId == orderId)
                          ?? throw new ApiRequestException(HttpStatusCode.NotFound, "The order was not found.");
            if (payment.Status == BankPaymentStatus.Cancelled)
            {
                throw new ApiRequestException(HttpStatusCode.Conflict, "The order was cancelled.");
            }

            payment.ShippedAt ??= _time.GetUtcNow();
            payment.Version++;
            await _db.SaveChangesAsync();
            return new OrderShippedDto(orderId, payment.ShippedAt.Value);
        }
        finally
        {
            gate.Release();
        }
    }
}
