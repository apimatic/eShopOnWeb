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

public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPayPalPaymentGateway _payPal;

    public ReconciliationService(IRepository<Order> orderRepository, IPayPalPaymentGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // PayPal's own record over the whole range (the gateway chunks/pages to cover it fully).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's own record: every order that has a captured payment.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);
        var capturedOrders = orders
            .Where(o => o.Payment?.CaptureId is not null)
            .ToList();

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = transactions.Count
        };

        // Index eShop orders by the identifiers that surface in PayPal's report.
        var ordersByCustomField = capturedOrders
            .GroupBy(o => o.Id.ToString())
            .ToDictionary(g => g.Key, g => g.First());
        var ordersByCaptureId = capturedOrders
            .Where(o => o.Payment!.CaptureId is not null)
            .GroupBy(o => o.Payment!.CaptureId!)
            .ToDictionary(g => g.Key, g => g.First());

        var matchedOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            Order? order = null;
            if (!string.IsNullOrEmpty(tx.CustomField) && ordersByCustomField.TryGetValue(tx.CustomField!, out var byCustom))
            {
                order = byCustom;
            }
            else if (!string.IsNullOrEmpty(tx.TransactionId) && ordersByCaptureId.TryGetValue(tx.TransactionId, out var byCapture))
            {
                order = byCapture;
            }

            if (order is null)
            {
                report.InPayPalOnly.Add(tx);
                continue;
            }

            var captured = order.Payment!.CapturedAmount ?? 0m;
            report.Matched.Add(new ReconciliationMatch
            {
                OrderId = order.Id,
                CaptureId = order.Payment.CaptureId,
                PayPalTransactionId = tx.TransactionId,
                OrderCapturedAmount = captured,
                PayPalAmount = tx.Amount,
                PayPalStatus = tx.Status,
                AmountsAgree = decimal.Round(Math.Abs(captured - Math.Abs(tx.Amount)), 2) == 0m
            });
            matchedOrderIds.Add(order.Id);
        }

        foreach (var order in capturedOrders.Where(o => !matchedOrderIds.Contains(o.Id)))
        {
            report.InEShopOnly.Add(new ReconciliationOrderRef
            {
                OrderId = order.Id,
                CaptureId = order.Payment!.CaptureId,
                CapturedAmount = order.Payment.CapturedAmount ?? 0m,
                PaymentStatus = order.Payment.Status.ToString()
            });
        }

        return report;
    }
}
