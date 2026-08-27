using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines up PayPal's own transaction records for a date range against eShop orders,
/// surfacing entries present on one side but not the other.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPalGateway;
    private readonly IRepository<OrderPayment> _paymentRepository;

    public ReconciliationService(IPayPalGateway payPalGateway, IRepository<OrderPayment> paymentRepository)
    {
        _payPalGateway = payPalGateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentConflictException("'to' must not be earlier than 'from'.");
        }

        var transactions = await _payPalGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsWithRefundsSpecification(), cancellationToken);

        // Map every PayPal-owned id we track to the eShop order it belongs to.
        var idToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            idToOrder.TryAdd(payment.PayPalOrderId, payment.OrderId);
            idToOrder.TryAdd(payment.AuthorizationId, payment.OrderId);
            if (payment.CaptureId != null) idToOrder.TryAdd(payment.CaptureId, payment.OrderId);
            foreach (var refund in payment.Refunds)
            {
                idToOrder.TryAdd(refund.PayPalRefundId, payment.OrderId);
            }
        }

        var report = new ReconciliationReport { From = from, To = to };
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            int? orderId = null;
            if (txn.TransactionId != null && idToOrder.TryGetValue(txn.TransactionId, out var byId))
            {
                orderId = byId;
            }
            else if (txn.ReferenceId != null && idToOrder.TryGetValue(txn.ReferenceId, out var byRef))
            {
                orderId = byRef;
            }
            else if (TryParseCustomId(txn.CustomId, out var byCustom))
            {
                orderId = byCustom;
            }

            if (txn.TransactionId != null) seenIds.Add(txn.TransactionId);
            if (txn.ReferenceId != null) seenIds.Add(txn.ReferenceId);

            report.Transactions.Add(new ReconciliationEntry
            {
                TransactionId = txn.TransactionId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Currency = txn.Currency,
                FeeAmount = txn.FeeAmount,
                TransactionTime = txn.InitiationDate,
                OrderId = orderId,
                MatchStatus = orderId.HasValue ? "Matched" : "MissingInEShop"
            });
        }

        foreach (var payment in payments.Where(p => p.CreatedAt >= from && p.CreatedAt <= to))
        {
            var knownToPayPal = seenIds.Contains(payment.PayPalOrderId)
                || seenIds.Contains(payment.AuthorizationId)
                || (payment.CaptureId != null && seenIds.Contains(payment.CaptureId));
            if (!knownToPayPal)
            {
                report.MissingFromPayPal.Add(new UnmatchedPayment
                {
                    OrderId = payment.OrderId,
                    PayPalOrderId = payment.PayPalOrderId,
                    AuthorizationId = payment.AuthorizationId,
                    CaptureId = payment.CaptureId,
                    Amount = payment.CapturedAmount ?? payment.AuthorizedAmount,
                    Currency = payment.Currency,
                    CreatedAt = payment.CreatedAt
                });
            }
        }

        return report;
    }

    private static bool TryParseCustomId(string? customId, out int orderId)
    {
        orderId = default;
        const string prefix = "eshop-order-";
        if (customId != null && customId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(customId.Substring(prefix.Length), out orderId);
        }
        return false;
    }
}
