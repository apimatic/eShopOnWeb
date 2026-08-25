using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _paymentGateway;

    public ReconciliationService(IRepository<Order> orderRepository, IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentGateway = paymentGateway;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, ct);
        var localOrders = await _orderRepository.ListAsync(new OrdersWithPaymentInDateRangeSpecification(from, to), ct);

        // PayPal's Transaction Search reports each money-movement event (capture, refund) under its own
        // transaction_id, which is the same id Payments v2 returned us as the capture id / refund id -
        // that is the reliable join key, verified against live sandbox data. (paypal_reference_id is
        // populated with type "TXN", pointing at a *preceding* transaction in the chain, not type "ODR"
        // pointing at the Orders v2 id - so matching on reference type "ODR" would never match anything.)
        var expectedByTransactionId = new Dictionary<string, (Order Order, decimal ExpectedAmount)>();
        foreach (var order in localOrders)
        {
            var payment = order.Payment;
            if (payment is null) continue;

            if (payment.CaptureId is not null && payment.CapturedAmount.HasValue)
                expectedByTransactionId[payment.CaptureId] = (order, payment.CapturedAmount.Value);

            foreach (var refund in payment.Refunds)
                expectedByTransactionId[refund.PayPalRefundId] = (order, -refund.Amount);
        }

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<GatewayTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var transaction in transactions)
        {
            if (expectedByTransactionId.TryGetValue(transaction.TransactionId, out var expected))
            {
                matchedOrderIds.Add(expected.Order.Id);
                var amountMismatch = Math.Abs(Math.Abs(transaction.Amount) - Math.Abs(expected.ExpectedAmount)) >= 0.01m;
                matched.Add(new ReconciliationMatch(expected.Order.Id, expected.Order.Payment!.PayPalOrderId, transaction, amountMismatch));
            }
            else
            {
                payPalOnly.Add(transaction);
            }
        }

        // eShop's own record of money that moved (captures and refunds) that PayPal's report did not return -
        // either a genuine mismatch, or (commonly in sandbox) reporting lag on very recent activity.
        var eshopOnly = localOrders
            .Where(o => o.Payment is not null && (o.Payment.CaptureId is not null || o.Payment.Refunds.Count > 0)
                        && !matchedOrderIds.Contains(o.Id))
            .ToList();

        return new ReconciliationReport(from, to, matched, payPalOnly, eshopOnly);
    }
}
