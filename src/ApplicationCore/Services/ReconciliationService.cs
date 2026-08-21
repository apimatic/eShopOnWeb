using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPal;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;

    public ReconciliationService(
        IPayPalGateway payPal,
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository)
    {
        _payPal = payPal;
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReportDto> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsWithRefundsSpec(), cancellationToken);
        var orders = await _orderRepository.ListAsync(cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        var matches = new List<ReconciliationMatchDto>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var payment = payments.FirstOrDefault(p => Matches(p, txn));
            if (payment == null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedPaymentIds.Add(payment.Id);
            matches.Add(new ReconciliationMatchDto
            {
                OrderId = payment.OrderId,
                PayPalOrderId = payment.PayPalOrderId,
                PayPalCaptureId = payment.PayPalCaptureId,
                PayPalAuthorizationId = payment.PayPalAuthorizationId,
                PayPalTransaction = txn
            });
        }

        var eShopOnly = payments
            .Where(p => IsInRange(p, ordersById, from, to) && HasPayPalFootprint(p) && !matchedPaymentIds.Contains(p.Id))
            .Select(p => new UnmatchedOrderPaymentDto
            {
                OrderId = p.OrderId,
                Status = p.Status.ToString(),
                Amount = p.Amount,
                Currency = p.Currency,
                PayPalOrderId = p.PayPalOrderId,
                PayPalAuthorizationId = p.PayPalAuthorizationId,
                PayPalCaptureId = p.PayPalCaptureId,
                OrderDate = ordersById.TryGetValue(p.OrderId, out var order) ? order.OrderDate : DateTimeOffset.MinValue
            })
            .ToList();

        return new ReconciliationReportDto
        {
            From = from,
            To = to,
            PayPalTransactionCount = paypalTransactions.Count,
            MatchedCount = matches.Count,
            Matches = matches,
            PayPalOnly = paypalOnly,
            EShopOnly = eShopOnly
        };
    }

    private static bool IsInRange(
        OrderPayment payment,
        IReadOnlyDictionary<int, Order> orders,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var timestamps = new List<DateTimeOffset>();
        if (orders.TryGetValue(payment.OrderId, out var order))
        {
            timestamps.Add(order.OrderDate);
        }

        if (payment.AuthorizationCreatedAt.HasValue) timestamps.Add(payment.AuthorizationCreatedAt.Value);
        if (payment.CapturedAt.HasValue) timestamps.Add(payment.CapturedAt.Value);
        timestamps.AddRange(payment.Refunds.Select(r => r.CreatedAt));

        return timestamps.Any(ts => ts >= from && ts <= to);
    }

    private static bool HasPayPalFootprint(OrderPayment payment)
    {
        return !string.IsNullOrEmpty(payment.PayPalOrderId)
            || !string.IsNullOrEmpty(payment.PayPalAuthorizationId)
            || !string.IsNullOrEmpty(payment.PayPalCaptureId)
            || payment.Refunds.Count > 0;
    }

    private static bool Matches(OrderPayment payment, PayPalReportedTransaction txn)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ids, payment.PayPalOrderId);
        Add(ids, payment.PayPalAuthorizationId);
        Add(ids, payment.PayPalCaptureId);
        Add(ids, payment.InvoiceId);
        Add(ids, InvoiceId(payment.OrderId));
        Add(ids, payment.OrderId.ToString(CultureInfo.InvariantCulture));
        foreach (var refund in payment.Refunds)
        {
            Add(ids, refund.PayPalRefundId);
        }

        return Contains(ids, txn.TransactionId)
            || Contains(ids, txn.PaypalReferenceId)
            || Contains(ids, txn.InvoiceId)
            || Contains(ids, txn.CustomField);
    }

    private static void Add(HashSet<string> ids, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ids.Add(value);
        }
    }

    private static bool Contains(HashSet<string> ids, string? value) =>
        !string.IsNullOrWhiteSpace(value) && ids.Contains(value);

    private static string InvoiceId(int orderId) => $"ESHOP-{orderId}";
}
