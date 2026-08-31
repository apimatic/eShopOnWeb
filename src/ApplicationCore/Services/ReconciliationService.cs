using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own transaction report up against eShop orders. Each payment carries a
/// unique invoice id into PayPal, which is the primary match key; the persisted
/// authorization, capture and refund ids serve as fallbacks when invoice_id is not echoed.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPaymentGateway paymentGateway, IRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), ct);

        var ordersByInvoiceId = orders
            .Where(o => o.Payment?.InvoiceId is not null)
            .GroupBy(o => o.Payment!.InvoiceId!)
            .ToDictionary(g => g.Key, g => g.First());

        // Provider transaction ids are the authorization/capture ids we persist,
        // which the report reliably carries even when invoice_id is not echoed.
        var ordersByProviderId = orders
            .Where(o => o.Payment is not null)
            .SelectMany(o => new[]
            {
                (ProviderId: o.Payment!.AuthorizationId, Order: o),
                (ProviderId: o.Payment!.CaptureId, Order: o)
            }.Concat(o.Payment!.Refunds.Select(r => (ProviderId: r.PayPalRefundId, Order: o))))
            .Where(x => x.ProviderId is not null)
            .GroupBy(x => x.ProviderId!)
            .ToDictionary(g => g.Key, g => g.First().Order);

        var report = new ReconciliationReport { From = from, To = to };
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var entry = new ReconciliationEntry
            {
                TransactionId = txn.TransactionId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Currency = txn.Currency,
                Fee = txn.Fee,
                InvoiceId = txn.InvoiceId,
                InitiationDate = txn.InitiationDate,
                UpdatedDate = txn.UpdatedDate
            };

            Order? matched = null;
            if (txn.InvoiceId is not null)
            {
                ordersByInvoiceId.TryGetValue(txn.InvoiceId, out matched);
            }
            if (matched is null && txn.TransactionId is not null)
            {
                ordersByProviderId.TryGetValue(txn.TransactionId, out matched);
            }
            if (matched is not null)
            {
                entry.MatchedOrderId = matched.Id;
                matchedOrderIds.Add(matched.Id);
            }

            report.Transactions.Add(entry);
        }

        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }
            var inRange = payment.CreatedAt >= from && payment.CreatedAt <= to;
            if (inRange && !matchedOrderIds.Contains(order.Id))
            {
                report.OrdersMissingFromProviderReport.Add(new ReconciliationOrder
                {
                    OrderId = order.Id,
                    BuyerId = order.BuyerId,
                    Status = order.Status.ToString(),
                    PayPalOrderId = payment.PayPalOrderId,
                    AuthorizationId = payment.AuthorizationId,
                    CaptureId = payment.CaptureId,
                    CapturedAmount = payment.CapturedAmount,
                    Currency = payment.Currency
                });
            }
        }

        return report;
    }
}
