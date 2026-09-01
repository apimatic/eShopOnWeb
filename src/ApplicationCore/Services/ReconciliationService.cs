using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IRepository<Order> _orderRepository;

    public ReconciliationService(IPaymentGateway paymentGateway, IRepository<Order> orderRepository)
    {
        _paymentGateway = paymentGateway;
        _orderRepository = orderRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        if (to <= from)
        {
            throw new BadRequestException("The 'to' date-time must be after the 'from' date-time.");
        }

        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), ct);

        var byPayPalOrderId = orders.Where(o => o.PayPalOrderId is not null)
            .ToDictionary(o => o.PayPalOrderId!, o => o, StringComparer.OrdinalIgnoreCase);
        var byAuthorizationId = orders.Where(o => o.AuthorizationId is not null)
            .ToDictionary(o => o.AuthorizationId!, o => o, StringComparer.OrdinalIgnoreCase);
        var byCaptureId = orders.Where(o => o.CaptureId is not null)
            .ToDictionary(o => o.CaptureId!, o => o, StringComparer.OrdinalIgnoreCase);
        var byRefundId = orders.SelectMany(o => o.Refunds.Select(r => (order: o, r.PayPalRefundId)))
            .ToDictionary(x => x.PayPalRefundId, x => x.order, StringComparer.OrdinalIgnoreCase);

        var report = new ReconciliationReport { From = from, To = to };
        var matchedOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            var line = new ReconciliationLine { Transaction = tx, OrderId = MatchOrder(tx) };
            if (line.OrderId is not null)
            {
                matchedOrderIds.Add(line.OrderId.Value);
                report.Transactions.Add(line);
            }
            else
            {
                report.UnmatchedTransactions.Add(line);
            }
        }

        report.OrdersMissingFromPayPalReport = orders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new UnmatchedOrderLine
            {
                OrderId = o.Id,
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                CaptureId = o.CaptureId,
                PaymentStatus = o.PaymentStatus.ToString(),
                OrderDate = o.OrderDate
            })
            .ToList();

        return report;

        int? MatchOrder(GatewayTransaction tx)
        {
            // PayPal types its reference id: ODR = order id, TXN = authorization/capture/refund id.
            if (tx.ReferenceId is not null)
            {
                var order = tx.ReferenceIdType switch
                {
                    "ODR" => byPayPalOrderId.GetValueOrDefault(tx.ReferenceId),
                    "TXN" => byAuthorizationId.GetValueOrDefault(tx.ReferenceId)
                             ?? byCaptureId.GetValueOrDefault(tx.ReferenceId)
                             ?? byRefundId.GetValueOrDefault(tx.ReferenceId),
                    _ => null
                };
                if (order is not null)
                {
                    return order.Id;
                }
            }

            // Fall back to the invoice id we stamped on the PayPal order at creation — but only
            // its exact shape ("order-{id}-{8 hex}"), and only when the transaction does not
            // predate the local order: on accounts that see activity from other systems the same
            // invoice shape can appear on unrelated transactions.
            var match = OurInvoiceId.Match(tx.InvoiceId ?? string.Empty);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var orderId))
            {
                var candidate = orders.FirstOrDefault(o => o.Id == orderId);
                if (candidate is not null
                    && (tx.InitiatedAt is null || tx.InitiatedAt >= candidate.OrderDate.AddMinutes(-5)))
                {
                    return orderId;
                }
            }
            return null;
        }
    }

    private static readonly Regex OurInvoiceId = new(@"^order-(\d+)-[0-9a-f]{8}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
}
