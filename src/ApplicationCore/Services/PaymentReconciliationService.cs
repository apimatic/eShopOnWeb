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

public class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IPayPalClient _payPalClient;
    private readonly IReadRepository<Order> _orderRepository;

    public PaymentReconciliationService(IPayPalClient payPalClient, IReadRepository<Order> orderRepository)
    {
        _payPalClient = payPalClient;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be greater than or equal to `from`.", 400);
        }

        var paypalTransactions = await _payPalClient.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInDateRangeSpecification(from, to), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindOrder(orders, txn);
            if (order is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matches.Add(new ReconciliationMatch { OrderId = order.Id, PayPalTransaction = txn });
        }

        var eshopOnly = orders
            .Where(o => o.Payment is not null && !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationEshopEntry
            {
                OrderId = o.Id,
                PayPalCaptureId = o.Payment!.CaptureId,
                PayPalAuthorizationId = o.Payment.AuthorizationId,
                Status = o.Status.ToString(),
                Amount = o.Payment.CapturedAmount ?? o.Payment.AuthorizedAmount
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matches = matches,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private static Order? FindOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(txn.CustomField) &&
                string.Equals(txn.CustomField, order.Id.ToString(), StringComparison.Ordinal))
            {
                return order;
            }

            if (!string.IsNullOrWhiteSpace(txn.InvoiceId) &&
                string.Equals(txn.InvoiceId, payment.InvoiceId, StringComparison.Ordinal))
            {
                return order;
            }

            if (IdsEqual(txn.TransactionId, payment.CaptureId) ||
                IdsEqual(txn.TransactionId, payment.AuthorizationId) ||
                IdsEqual(txn.ReferenceId, payment.CaptureId) ||
                IdsEqual(txn.ReferenceId, payment.AuthorizationId) ||
                IdsEqual(txn.ReferenceId, payment.PayPalOrderId) ||
                payment.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId) || IdsEqual(txn.ReferenceId, r.PayPalRefundId)))
            {
                return order;
            }
        }

        return null;
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
