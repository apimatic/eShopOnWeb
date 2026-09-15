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

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("'to' must be on or after 'from'.", 400);
        }

        var paypal = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(
            new OrdersWithPaymentsInRangeSpecification(from, to), cancellationToken);

        var eshop = orders
            .Where(o => o.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            .Select(ToRecord)
            .ToList();

        var matched = new List<ReconciliationMatch>();
        var matchedPaypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var order in eshop)
        {
            var hit = paypal.FirstOrDefault(t => Matches(order, t));
            if (hit is null)
            {
                continue;
            }

            matched.Add(new ReconciliationMatch
            {
                OrderId = order.OrderId,
                PayPalTransactionId = hit.TransactionId,
                MatchReason = MatchReason(order, hit)
            });
            matchedOrderIds.Add(order.OrderId);
            if (!string.IsNullOrEmpty(hit.TransactionId))
            {
                matchedPaypalIds.Add(hit.TransactionId);
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactions = paypal,
            EshopPayments = eshop,
            Matched = matched,
            PayPalOnly = paypal.Where(t =>
                string.IsNullOrEmpty(t.TransactionId) || !matchedPaypalIds.Contains(t.TransactionId)).ToList(),
            EshopOnly = eshop.Where(o => !matchedOrderIds.Contains(o.OrderId)).ToList()
        };
    }

    private static EshopPaymentRecord ToRecord(Order order) => new()
    {
        OrderId = order.Id,
        BuyerId = order.BuyerId,
        PaymentStatus = order.PaymentStatus.ToString(),
        PayPalOrderId = order.PayPalOrderId,
        PayPalAuthorizationId = order.PayPalAuthorizationId,
        PayPalCaptureId = order.PayPalCaptureId,
        OrderTotal = order.Total(),
        CapturedAmount = order.CapturedAmount,
        OrderDate = order.OrderDate
    };

    private static bool Matches(EshopPaymentRecord order, PayPalReportedTransaction txn)
    {
        if (InvoiceMatches(txn.InvoiceId, order.OrderId) || IdsEqual(txn.CustomField, $"ESHOP-{order.OrderId}"))
        {
            return true;
        }

        return IdsEqual(txn.TransactionId, order.PayPalCaptureId)
            || IdsEqual(txn.TransactionId, order.PayPalAuthorizationId)
            || IdsEqual(txn.PaypalReferenceId, order.PayPalOrderId)
            || IdsEqual(txn.PaypalReferenceId, order.PayPalCaptureId)
            || IdsEqual(txn.PaypalReferenceId, order.PayPalAuthorizationId);
    }

    private static string MatchReason(EshopPaymentRecord order, PayPalReportedTransaction txn)
    {
        if (InvoiceMatches(txn.InvoiceId, order.OrderId)) return "invoice_id";
        if (IdsEqual(txn.CustomField, $"ESHOP-{order.OrderId}")) return "custom_id";
        if (IdsEqual(txn.TransactionId, order.PayPalCaptureId)) return "capture_id";
        if (IdsEqual(txn.TransactionId, order.PayPalAuthorizationId)) return "authorization_id";
        return "paypal_reference";
    }

    private static bool InvoiceMatches(string? invoiceId, int orderId) =>
        !string.IsNullOrEmpty(invoiceId)
        && invoiceId.StartsWith($"ESHOP-{orderId}-", StringComparison.OrdinalIgnoreCase);

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrEmpty(left)
        && !string.IsNullOrEmpty(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
