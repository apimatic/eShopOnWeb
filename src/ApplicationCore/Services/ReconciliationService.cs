using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orders;
    private readonly IPaymentGateway _payments;

    public ReconciliationService(IReadRepository<Order> orders, IPaymentGateway payments)
    {
        _orders = orders;
        _payments = payments;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var paypal = await _payments.SearchTransactionsAsync(from, to, ct);
        var orders = await _orders.ListAsync(new PaidOrdersInRangeSpecification(from, to), ct);

        var eshopByPayPalId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            Add(order.PayPalOrderId, order);
            Add(order.PayPalAuthorizationId, order);
            Add(order.PayPalCaptureId, order);
            foreach (var refund in order.Refunds)
                Add(refund.PayPalRefundId, order);
            Add(order.Id.ToString(), order);
        }

        var matchedPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();
        var rows = new List<ReconciliationRow>();

        foreach (var txn in paypal)
        {
            Order? match = null;
            if (!string.IsNullOrEmpty(txn.TransactionId) && eshopByPayPalId.TryGetValue(txn.TransactionId, out var byTxn))
                match = byTxn;
            else if (!string.IsNullOrEmpty(txn.ReferenceId) && eshopByPayPalId.TryGetValue(txn.ReferenceId, out var byRef))
                match = byRef;
            else if (!string.IsNullOrEmpty(txn.InvoiceId) && eshopByPayPalId.TryGetValue(txn.InvoiceId, out var byInv))
                match = byInv;
            else if (!string.IsNullOrEmpty(txn.CustomField) && eshopByPayPalId.TryGetValue(txn.CustomField, out var byCustom))
                match = byCustom;

            if (match is not null)
            {
                matchedPayPalIds.Add(txn.TransactionId);
                matchedOrderIds.Add(match.Id);
                rows.Add(Row("matched", match, txn));
            }
            else
            {
                rows.Add(Row("paypal_only", null, txn));
            }
        }

        foreach (var order in orders)
        {
            if (matchedOrderIds.Contains(order.Id))
                continue;
            rows.Add(new ReconciliationRow(
                "eshop_only",
                order.Id.ToString(),
                order.PaymentStatus.ToString(),
                order.PayPalCaptureId ?? order.PayPalAuthorizationId,
                order.PayPalOrderId,
                order.Id.ToString(),
                (order.CapturedAmount ?? order.Total()).ToString("0.00"),
                order.PaypalFee?.ToString("0.00"),
                order.PayPalCaptureStatus ?? order.PayPalAuthorizationStatus,
                order.OrderDate.ToString("o")));
        }

        return new ReconciliationReport(
            from,
            to,
            paypal.Count,
            orders.Count,
            rows.Count(r => r.Match == "matched"),
            rows.Count(r => r.Match == "paypal_only"),
            rows.Count(r => r.Match == "eshop_only"),
            rows);

        void Add(string? key, Order order)
        {
            if (!string.IsNullOrEmpty(key) && !eshopByPayPalId.ContainsKey(key))
                eshopByPayPalId[key] = order;
        }
    }

    private static ReconciliationRow Row(string match, Order? order, ProviderTransaction txn) =>
        new(match,
            order?.Id.ToString(),
            order?.PaymentStatus.ToString(),
            txn.TransactionId,
            txn.ReferenceId,
            txn.InvoiceId ?? txn.CustomField,
            txn.Amount,
            txn.FeeAmount,
            txn.Status,
            txn.InitiationDate);
}
