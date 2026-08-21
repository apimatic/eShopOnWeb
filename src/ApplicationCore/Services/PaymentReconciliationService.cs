using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalPaymentsGateway _payPal;

    public PaymentReconciliationService(IRepository<Order> orderRepository, IPayPalPaymentsGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<PaymentReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var rangeOrders = await _orderRepository.ListAsync(new OrdersInDateRangeSpecification(from, to), cancellationToken);
        var paidOrders = await _orderRepository.ListAsync(new OrdersWithPayPalIdentifiersSpecification(), cancellationToken);

        var eShopRecords = rangeOrders.Concat(paidOrders)
            .GroupBy(o => o.Id)
            .Select(g => g.First())
            .Select(ToRecord)
            .ToList();

        var matched = new List<ReconciledTransaction>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();
        var matchedTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTransactions)
        {
            var match = eShopRecords.FirstOrDefault(r => Matches(r, txn));
            if (match is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matched.Add(new ReconciledTransaction { PayPal = txn, EShop = match });
            matchedOrderIds.Add(match.OrderId);
            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                matchedTxnIds.Add(txn.TransactionId);
            }
        }

        var eShopOnly = eShopRecords
            .Where(r => HasPaymentIdentifiers(r) && !matchedOrderIds.Contains(r.OrderId))
            .Where(r => r.OrderDate >= from && r.OrderDate <= to)
            .ToList();

        return new PaymentReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EShopOnly = eShopOnly
        };
    }

    private static bool HasPaymentIdentifiers(EShopPaymentRecord record)
        => !string.IsNullOrEmpty(record.PayPalOrderId)
           || !string.IsNullOrEmpty(record.AuthorizationId)
           || !string.IsNullOrEmpty(record.CaptureId)
           || record.RefundIds.Count > 0;

    private static bool Matches(EShopPaymentRecord record, PayPalReportedTransaction txn)
    {
        if (!string.IsNullOrEmpty(txn.TransactionId) && Identifiers(record).Contains(txn.TransactionId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.PayPalReferenceId) && Identifiers(record).Contains(txn.PayPalReferenceId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.InvoiceId) && string.Equals(txn.InvoiceId, PayPalOrderIdentifiers.InvoiceId(record.OrderId), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(txn.CustomField) && string.Equals(txn.CustomField, PayPalOrderIdentifiers.CustomId(record.OrderId), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static HashSet<string> Identifiers(EShopPaymentRecord record)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ids, record.PayPalOrderId);
        Add(ids, record.AuthorizationId);
        Add(ids, record.CaptureId);
        foreach (var refundId in record.RefundIds)
        {
            Add(ids, refundId);
        }

        return ids;
    }

    private static void Add(HashSet<string> ids, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ids.Add(value);
        }
    }

    private static EShopPaymentRecord ToRecord(Order order)
        => new()
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.AuthorizationId,
            CaptureId = order.CaptureId,
            RefundIds = order.Refunds.Select(r => r.PayPalRefundId).Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToList(),
            Total = order.Total(),
            OrderDate = order.OrderDate
        };
}
