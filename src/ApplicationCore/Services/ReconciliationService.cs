using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalClient _payPalClient;
    private readonly IReadRepository<Order> _orderRepository;

    public ReconciliationService(IPayPalClient payPalClient, IReadRepository<Order> orderRepository)
    {
        _payPalClient = payPalClient;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // PayPal's side: every transaction it reports across the whole range (paged + date-chunked).
        var transactions = await _payPalClient.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's side: all orders that carry a payment, indexed by the references PayPal echoes back.
        var orders = await _orderRepository.ListAsync(cancellationToken);
        var paidOrders = orders.Where(o => o.Payment is not null).ToList();

        var ordersByReference = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var ordersByCaptureId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var ordersByPayPalOrderId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in paidOrders)
        {
            var payment = order.Payment!;
            if (!string.IsNullOrEmpty(payment.MerchantReference)) ordersByReference[payment.MerchantReference] = order;
            if (!string.IsNullOrEmpty(payment.CaptureId)) ordersByCaptureId[payment.CaptureId!] = order;
            if (!string.IsNullOrEmpty(payment.PayPalOrderId)) ordersByPayPalOrderId[payment.PayPalOrderId!] = order;
        }

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<UnmatchedPayPalTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var order = MatchOrder(txn, ordersByReference, ordersByCaptureId, ordersByPayPalOrderId);
            if (order is not null)
            {
                matched.Add(new ReconciliationMatch(order.Id, txn.TransactionId, txn.EventCode, txn.Status, txn.Amount, txn.Currency));
                matchedOrderIds.Add(order.Id);
            }
            else
            {
                payPalOnly.Add(new UnmatchedPayPalTransaction(
                    txn.TransactionId, txn.EventCode, txn.Status, txn.Amount, txn.Currency, txn.InvoiceId, txn.InitiationDate));
            }
        }

        // eShop captures dated within the range that PayPal did not report back.
        var eShopOnly = paidOrders
            .Where(o => !string.IsNullOrEmpty(o.Payment!.CaptureId))
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new UnmatchedEShopOrder(
                o.Id, o.Payment!.MerchantReference, o.Payment!.CaptureId,
                o.Payment!.CapturedGross ?? o.Payment!.Amount, o.Payment!.Currency))
            .ToList();

        var note = "PayPal transaction reporting can lag live activity by up to a few hours, so a range " +
                   "covering payments just created may legitimately come back empty or partial on the PayPal side.";

        return new ReconciliationReport(from, to, transactions.Count, matched, payPalOnly, eShopOnly, note);
    }

    private static Order? MatchOrder(
        PayPalTransaction txn,
        IReadOnlyDictionary<string, Order> byReference,
        IReadOnlyDictionary<string, Order> byCaptureId,
        IReadOnlyDictionary<string, Order> byPayPalOrderId)
    {
        if (!string.IsNullOrEmpty(txn.InvoiceId) && byReference.TryGetValue(txn.InvoiceId!, out var byInvoice))
        {
            return byInvoice;
        }
        if (!string.IsNullOrEmpty(txn.CustomField) && byReference.TryGetValue(txn.CustomField!, out var byCustom))
        {
            return byCustom;
        }
        if (!string.IsNullOrEmpty(txn.TransactionId) && byCaptureId.TryGetValue(txn.TransactionId!, out var byCapture))
        {
            return byCapture;
        }
        if (!string.IsNullOrEmpty(txn.ReferenceId) && byPayPalOrderId.TryGetValue(txn.ReferenceId!, out var byPpOrder))
        {
            return byPpOrder;
        }
        return null;
    }
}
