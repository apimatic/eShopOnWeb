using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class ReconciliationService
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;

    public ReconciliationService(CatalogContext db, IPayPalClient payPal)
    {
        _db = db;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw new PaymentApiException(400, "INVALID_DATE_RANGE", "from must be earlier than to.");
        if (to > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new PaymentApiException(400, "INVALID_DATE_RANGE", "to cannot be in the future.");

        IReadOnlyList<PayPalTransaction> remote;
        try
        {
            remote = await _payPal.SearchAllTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            var suffix = ex.DebugId is null ? string.Empty : $" PayPal debug ID: {ex.DebugId}.";
            throw new PaymentApiException(502, ex.Issue ?? ex.Name ?? "PAYPAL_REPORTING_ERROR", ex.Message + suffix);
        }

        var orders = await _db.Orders.AsNoTracking().Include(x => x.Payment!).ThenInclude(x => x.Refunds)
            .Where(x => x.Payment != null).ToListAsync(cancellationToken);
        var entries = new List<ReconciliationEntry>();
        var reportingCutoff = DateTimeOffset.UtcNow.AddHours(-3);

        foreach (var transaction in remote)
        {
            var order = FindOrder(orders, transaction);
            entries.Add(new ReconciliationEntry("PayPal", order is null ? "MissingInEShop" : "Matched",
                order?.Id, Classify(order, transaction.Id), transaction.Id, transaction.Status,
                transaction.Amount, transaction.Fee, transaction.InitiatedAt, transaction.InvoiceId,
                transaction.EventCode));
        }

        var remoteIds = remote.SelectMany(x => new[] { x.Id, x.ReferenceId }).Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (payment.CaptureId is not null && payment.CapturedAt >= from && payment.CapturedAt <= to &&
                !remoteIds.Contains(payment.CaptureId))
            {
                var matchStatus = payment.CapturedAt > reportingCutoff ? "PendingPayPalReporting" : "MissingInPayPal";
                entries.Add(new ReconciliationEntry("EShop", matchStatus, order.Id, "Capture",
                    payment.CaptureId, payment.CaptureStatus, payment.CapturedAmount, payment.PayPalFee,
                    payment.CapturedAt, payment.InvoiceId, null));
            }

            foreach (var refund in payment.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to &&
                         !remoteIds.Contains(x.PayPalRefundId)))
            {
                var matchStatus = refund.CreatedAt > reportingCutoff ? "PendingPayPalReporting" : "MissingInPayPal";
                entries.Add(new ReconciliationEntry("EShop", matchStatus, order.Id, "Refund",
                    refund.PayPalRefundId, refund.Status, refund.Amount, null, refund.CreatedAt,
                    payment.InvoiceId, null));
            }
        }

        var ordered = entries.OrderBy(x => x.OccurredAt).ThenBy(x => x.PayPalTransactionId).ToList();
        return new ReconciliationReport(from, to, ordered.Count(x => x.MatchStatus == "Matched"),
            ordered.Count(x => x.MatchStatus == "MissingInEShop"),
            ordered.Count(x => x.MatchStatus == "MissingInPayPal"),
            ordered.Count(x => x.MatchStatus == "PendingPayPalReporting"), ordered);
    }

    private static Order? FindOrder(IReadOnlyList<Order> orders, PayPalTransaction transaction)
    {
        var byId = orders.FirstOrDefault(x => x.Payment?.CaptureId == transaction.Id ||
            x.Payment?.AuthorizationId == transaction.Id || x.Payment?.PayPalOrderId == transaction.Id ||
            x.Payment?.Refunds.Any(r => r.PayPalRefundId == transaction.Id) == true ||
            x.Payment?.CaptureId == transaction.ReferenceId || x.Payment?.AuthorizationId == transaction.ReferenceId ||
            x.Payment?.PayPalOrderId == transaction.ReferenceId);
        if (byId is not null) return byId;
        if (transaction.InvoiceId is not null)
            return orders.FirstOrDefault(x => x.Payment?.InvoiceId == transaction.InvoiceId);
        return null;
    }

    private static string Classify(Order? order, string transactionId)
    {
        if (order?.Payment?.CaptureId == transactionId) return "Capture";
        if (order?.Payment?.AuthorizationId == transactionId) return "Authorization";
        if (order?.Payment?.Refunds.Any(x => x.PayPalRefundId == transactionId) == true) return "Refund";
        return "Transaction";
    }
}

public sealed record ReconciliationReport(DateTimeOffset From, DateTimeOffset To, int Matched,
    int MissingInEShop, int MissingInPayPal, int PendingPayPalReporting,
    IReadOnlyList<ReconciliationEntry> Entries);
public sealed record ReconciliationEntry(string Source, string MatchStatus, int? OrderId, string Type,
    string PayPalTransactionId, string? Status, decimal? Amount, decimal? PayPalFee,
    DateTimeOffset? OccurredAt, string? InvoiceId, string? EventCode);
