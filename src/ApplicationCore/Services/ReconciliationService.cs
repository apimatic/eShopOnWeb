using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own transaction records for a date range up against eShop's payment references,
/// surfacing anything one side knows about and the other does not.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPal;

    public ReconciliationService(IReadRepository<Order> orderRepository, IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // PayPal's side, paged through the whole range.
        var payPalTransactions = await _payPal.SearchTransactionsAsync(from, to, ct);
        var payPalById = payPalTransactions
            .Where(t => !string.IsNullOrEmpty(t.TransactionId))
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop's side: every payment reference (hold / capture / refund) that falls in the range.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), ct);
        var eShopReferences = CollectReferences(orders, from, to).ToList();
        var eShopIds = new HashSet<string>(eShopReferences.Select(r => r.EShopReferenceId!), StringComparer.OrdinalIgnoreCase);

        var report = new ReconciliationReport
        {
            From = from.ToString("o"),
            To = to.ToString("o"),
            PayPalTransactionCount = payPalById.Count,
            EShopReferenceCount = eShopReferences.Count
        };

        // Matched + eShop-only, walking the eShop references.
        foreach (var reference in eShopReferences)
        {
            if (payPalById.TryGetValue(reference.EShopReferenceId!, out var tx))
            {
                report.Matched.Add(reference with
                {
                    PayPalTransactionId = tx.TransactionId,
                    PayPalStatus = tx.Status,
                    PayPalAmount = tx.Amount,
                    Currency = tx.Currency ?? reference.Currency
                });
            }
            else
            {
                report.InEShopOnly.Add(reference);
            }
        }

        // PayPal-only: transactions with no matching eShop reference.
        foreach (var (id, tx) in payPalById)
        {
            if (!eShopIds.Contains(id))
            {
                report.InPayPalOnly.Add(new ReconciliationRow
                {
                    PayPalTransactionId = tx.TransactionId,
                    PayPalStatus = tx.Status,
                    PayPalAmount = tx.Amount,
                    Currency = tx.Currency
                });
            }
        }

        return report;
    }

    private static IEnumerable<ReconciliationRow> CollectReferences(IEnumerable<Order> orders, DateTimeOffset from, DateTimeOffset to)
    {
        bool InRange(DateTimeOffset when) => when >= from && when <= to;

        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }

            // The hold, dated at order placement (closest timestamp we own for it).
            if (!string.IsNullOrEmpty(payment.AuthorizationId) && InRange(order.OrderDate))
            {
                yield return new ReconciliationRow
                {
                    OrderId = order.Id,
                    EShopReferenceType = "AUTHORIZATION",
                    EShopReferenceId = payment.AuthorizationId,
                    Currency = payment.Currency,
                    PayPalAmount = payment.AuthorizedAmount
                };
            }

            // The capture.
            if (!string.IsNullOrEmpty(payment.CaptureId) && payment.CapturedAt is DateTimeOffset capturedAt && InRange(capturedAt))
            {
                yield return new ReconciliationRow
                {
                    OrderId = order.Id,
                    EShopReferenceType = "CAPTURE",
                    EShopReferenceId = payment.CaptureId,
                    Currency = payment.Currency,
                    PayPalAmount = payment.CapturedAmount
                };
            }

            // The refunds.
            foreach (var refund in payment.Refunds)
            {
                if (InRange(refund.CreatedAt))
                {
                    yield return new ReconciliationRow
                    {
                        OrderId = order.Id,
                        EShopReferenceType = "REFUND",
                        EShopReferenceId = refund.RefundId,
                        Currency = payment.Currency,
                        PayPalAmount = refund.Amount
                    };
                }
            }
        }
    }
}
