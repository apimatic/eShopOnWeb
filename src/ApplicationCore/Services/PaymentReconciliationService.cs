using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IPayPalGateway _payPalGateway;
    private readonly IRepository<Order> _orderRepository;

    public PaymentReconciliationService(IPayPalGateway payPalGateway, IRepository<Order> orderRepository)
    {
        _payPalGateway = payPalGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var paypalTransactions = await _payPalGateway.SearchAllTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        var matches = new List<ReconciledMatch>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transaction in paypalTransactions)
        {
            var order = orders.FirstOrDefault(o => Matches(o, transaction));
            if (order is null)
            {
                paypalOnly.Add(transaction);
                continue;
            }

            matches.Add(new ReconciledMatch(order.Id, transaction));
            matchedTransactionIds.Add(transaction.TransactionId);
        }

        var eshopOnly = orders
            .Where(o => o.Payment is not null)
            .Where(o => HasActivityInRange(o, from, to))
            .Select(ToRecord)
            .Where(record => !IsRepresented(record, matchedTransactionIds, paypalTransactions))
            .ToList();

        return new ReconciliationReport(from, to, paypalTransactions, matches, paypalOnly, eshopOnly);
    }

    private static bool Matches(Order order, PayPalReportedTransaction transaction)
    {
        var payment = order.Payment;
        if (payment is null)
        {
            return false;
        }

        if (IdsEqual(payment.CaptureId, transaction.TransactionId)
            || IdsEqual(payment.AuthorizationId, transaction.TransactionId)
            || IdsEqual(payment.PayPalOrderId, transaction.TransactionId)
            || IdsEqual(payment.CaptureId, transaction.ReferenceId)
            || IdsEqual(payment.AuthorizationId, transaction.ReferenceId)
            || IdsEqual(payment.PayPalOrderId, transaction.ReferenceId))
        {
            return true;
        }

        if (order.Refunds.Any(r => IdsEqual(r.PayPalRefundId, transaction.TransactionId) || IdsEqual(r.PayPalRefundId, transaction.ReferenceId)))
        {
            return true;
        }

        if (IdsEqual(order.Id.ToString(), transaction.CustomField)
            || IdsEqual($"ESHOP-{order.PaymentCorrelationId}", transaction.InvoiceId)
            || IdsEqual(order.Id.ToString(), transaction.InvoiceId))
        {
            return true;
        }

        return false;
    }

    private static bool HasActivityInRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        var payment = order.Payment;
        if (payment is null)
        {
            return false;
        }

        return InRange(payment.AuthorizedAt, from, to)
            || InRange(payment.CapturedAt, from, to)
            || InRange(payment.VoidedAt, from, to)
            || order.Refunds.Any(r => InRange(r.CreatedAt, from, to));
    }

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to) =>
        value is not null && value >= from && value <= to;

    private static EshopPaymentRecord ToRecord(Order order)
    {
        return new EshopPaymentRecord(
            order.Id,
            order.Status.ToString(),
            order.Payment?.PayPalOrderId,
            order.Payment?.AuthorizationId,
            order.Payment?.CaptureId,
            order.Refunds.Select(r => r.PayPalRefundId).ToList());
    }

    private static bool IsRepresented(EshopPaymentRecord record, HashSet<string> matchedTransactionIds, IReadOnlyList<PayPalReportedTransaction> transactions)
    {
        var ids = new[] { record.PayPalOrderId, record.AuthorizationId, record.CaptureId }
            .Concat(record.RefundIds)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!);

        if (ids.Any(matchedTransactionIds.Contains))
        {
            return true;
        }

        return transactions.Any(t =>
            ids.Contains(t.TransactionId, StringComparer.OrdinalIgnoreCase)
            || ids.Contains(t.ReferenceId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            || string.Equals(t.CustomField, record.OrderId.ToString(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.InvoiceId, $"ESHOP-{record.OrderId}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
