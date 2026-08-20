using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPal;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalGateway payPal, IRepository<Order> orderRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpec(), cancellationToken);

        var eshopRecords = orders
            .Select(ToRecord)
            .Where(r => HasPayment(r) || (r.OrderDate >= from && r.OrderDate <= to))
            .ToList();

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var usedEshop = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var match = FindMatch(txn, eshopRecords);
            if (match is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matched.Add(new ReconciliationMatch(txn, match));
            usedEshop.Add(match.OrderId);
        }

        var eshopOnly = eshopRecords
            .Where(r => HasPayment(r) && !usedEshop.Contains(r.OrderId) && r.OrderDate >= from && r.OrderDate <= to)
            .ToList();

        return new ReconciliationReport(from, to, matched, paypalOnly, eshopOnly);
    }

    private static EShopPaymentRecord ToRecord(Order order)
    {
        return new EShopPaymentRecord(
            order.Id,
            order.Status.ToString(),
            order.Payment?.PayPalOrderId,
            order.Payment?.AuthorizationId,
            order.Payment?.CaptureId,
            order.Refunds.Select(r => r.PayPalRefundId).ToList(),
            MoneyFormat.ToCents(order.Total()),
            order.Payment?.Currency,
            order.OrderDate);
    }

    private static bool HasPayment(EShopPaymentRecord record) =>
        !string.IsNullOrWhiteSpace(record.PayPalOrderId)
        || !string.IsNullOrWhiteSpace(record.AuthorizationId)
        || !string.IsNullOrWhiteSpace(record.CaptureId)
        || record.RefundIds.Count > 0;

    private static EShopPaymentRecord? FindMatch(PayPalReportedTransaction txn, IReadOnlyList<EShopPaymentRecord> records)
    {
        foreach (var record in records)
        {
            if (IdsEqual(txn.TransactionId, record.PayPalOrderId)
                || IdsEqual(txn.TransactionId, record.AuthorizationId)
                || IdsEqual(txn.TransactionId, record.CaptureId)
                || record.RefundIds.Any(id => IdsEqual(txn.TransactionId, id))
                || IdsEqual(txn.PayPalReferenceId, record.PayPalOrderId)
                || IdsEqual(txn.PayPalReferenceId, record.AuthorizationId)
                || IdsEqual(txn.PayPalReferenceId, record.CaptureId)
                || (txn.InvoiceId is not null && txn.InvoiceId.StartsWith($"eshop-{record.OrderId}-", StringComparison.OrdinalIgnoreCase))
                || IdsEqual(txn.CustomField, record.OrderId.ToString()))
            {
                return record;
            }
        }

        return null;
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
