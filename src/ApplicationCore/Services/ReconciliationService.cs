using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private const string InvoicePrefix = "ESHOP-";

    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalClient _payPal;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalClient payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new PaymentValidationException("'to' must be the same as or after 'from'.");

        // PayPal's own record for the range (chunked + fully paged by the client).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's record: every order that has an attached PayPal payment.
        var orders = await _orderRepository.ListAsync(cancellationToken);
        var paidOrders = orders.Where(o => o.Payment is not null).ToList();

        // Index eShop orders by their PayPal invoice id (ESHOP-{orderId}).
        var ordersByInvoice = paidOrders.ToDictionary(o => InvoiceIdFor(o.Id), o => o, StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<ReconciliationPayPalEntry>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            if (!string.IsNullOrEmpty(txn.InvoiceId) && ordersByInvoice.TryGetValue(txn.InvoiceId!, out var order))
            {
                var captured = order.Payment!.CapturedAmount;
                matchedOrderIds.Add(order.Id);
                matched.Add(new ReconciliationMatch
                {
                    OrderId = order.Id,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalStatus = txn.Status,
                    PayPalAmount = txn.Amount,
                    EshopCapturedAmount = captured,
                    Currency = txn.Currency ?? order.Payment!.Currency,
                    AmountsAgree = captured.HasValue && decimal.Round(captured.Value, 2) == decimal.Round(txn.Amount, 2)
                });
            }
            else
            {
                payPalOnly.Add(new ReconciliationPayPalEntry
                {
                    PayPalTransactionId = txn.TransactionId,
                    InvoiceId = txn.InvoiceId,
                    Status = txn.Status,
                    Amount = txn.Amount,
                    Currency = txn.Currency,
                    Date = txn.InitiationDate
                });
            }
        }

        // Captured eShop orders that PayPal's report for this range does not show.
        var eshopOnly = paidOrders
            .Where(o => o.Payment!.IsCaptured && !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationEshopEntry
            {
                OrderId = o.Id,
                CaptureId = o.Payment!.CaptureId,
                CapturedAmount = o.Payment!.CapturedAmount,
                Currency = o.Payment!.Currency,
                InvoiceId = InvoiceIdFor(o.Id)
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            InPayPalNotInEshop = payPalOnly,
            InEshopNotInPayPal = eshopOnly
        };
    }

    private static string InvoiceIdFor(int orderId) =>
        string.Create(CultureInfo.InvariantCulture, $"{InvoicePrefix}{orderId}");
}
