using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Builds the operator's reconciliation report: lines up PayPal's own transaction record
/// for a date range against eShop orders that hold payment state, so a payment known to one
/// side and not the other becomes visible in both directions.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IRepository<Order> orderRepository,
        IPaymentGateway gateway,
        IAppLogger<ReconciliationService> logger)
    {
        _orderRepository = orderRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to <= from)
        {
            throw new ValidationFailureException("The reconciliation range 'to' must be after 'from'.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(ct);

        var candidates = orders
            .Where(o => o.Payment is not null && HasActivityIn(o, from, to))
            .ToList();

        var rows = new List<ReconciliationRow>();
        var consumedTransactions = new HashSet<string>(StringComparer.Ordinal);
        var matchedOrders = new HashSet<int>();

        foreach (var order in candidates)
        {
            var orderKeys = BuildOrderKeys(order);
            var match = transactions.FirstOrDefault(t =>
                !consumedTransactions.Contains(t.TransactionId) &&
                KeysIntersect(orderKeys, t));

            if (match is not null)
            {
                consumedTransactions.Add(match.TransactionId);
                matchedOrders.Add(order.Id);
                rows.Add(ToRow(ReconciliationMatchState.Matched, match, order));
            }
            else
            {
                rows.Add(ToRow(ReconciliationMatchState.EshopOnly, null, order));
            }
        }

        foreach (var orphan in transactions.Where(t => !consumedTransactions.Contains(t.TransactionId)))
        {
            rows.Add(ToRow(ReconciliationMatchState.PayPalOnly, orphan, null));
        }

        var report = new ReconciliationReport(
            from,
            to,
            DateTimeOffset.UtcNow,
            rows,
            rows.Count(r => r.MatchState == ReconciliationMatchState.Matched),
            rows.Count(r => r.MatchState == ReconciliationMatchState.PayPalOnly),
            rows.Count(r => r.MatchState == ReconciliationMatchState.EshopOnly),
            CoverageNote: "PayPal's transaction report can lag live activity by up to 3 hours; an empty result for a recent range is expected, not a missing payment.");

        _logger.LogInformation($"Reconciliation {from:O}..{to:O}: {report.MatchedCount} matched, {report.PayPalOnlyCount} PayPal-only, {report.EshopOnlyCount} eShop-only.");
        return report;
    }

    private static bool HasActivityIn(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        var moments = new List<DateTimeOffset> { order.OrderDate };
        if (order.Payment is { } p)
        {
            if (p.AuthorizationCreatedTime is { } created) moments.Add(created);
            if (p.CapturedAt is { } captured) moments.Add(captured);
        }
        moments.AddRange(order.Refunds.Select(r => r.RefundedAt));
        return moments.Any(m => m >= from && m <= to);
    }

    private static HashSet<string> BuildOrderKeys(Order order)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { OrderInvoice(order.Id) };
        if (order.Payment is { } p)
        {
            AddIfPresent(keys, p.ProviderOrderId);
            AddIfPresent(keys, p.AuthorizationId);
            AddIfPresent(keys, p.CaptureId);
        }
        foreach (var refund in order.Refunds)
        {
            AddIfPresent(keys, refund.ProviderRefundId);
            AddIfPresent(keys, OrderInvoice(order.Id));
        }
        return keys;
    }

    private static string OrderInvoice(int orderId) => $"eshop-order-{orderId}";

    private static void AddIfPresent(HashSet<string> keys, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            keys.Add(value);
        }
    }

    private static bool KeysIntersect(HashSet<string> orderKeys, GatewayTransaction transaction)
    {
        var rowKeys = new[] { transaction.TransactionId, transaction.PaypalReferenceId, transaction.InvoiceId, transaction.CustomField };
        return rowKeys.Any(k => !string.IsNullOrEmpty(k) && orderKeys.Contains(k!));
    }

    private static ReconciliationRow ToRow(ReconciliationMatchState state, GatewayTransaction? transaction, Order? order)
    {
        string? paymentSummary = null;
        if (order?.Payment is { } p)
        {
            paymentSummary = $"auth:{p.AuthorizationId} ({p.AuthorizationStatus}), capture:{(string.IsNullOrEmpty(p.CaptureId) ? "-" : p.CaptureId)} {p.CaptureStatus}, refunds:{order.Refunds.Count}";
        }

        return new ReconciliationRow(
            state,
            transaction?.TransactionId,
            transaction?.TransactionStatus,
            transaction?.TransactionEventCode,
            transaction?.Amount,
            transaction?.FeeAmount,
            transaction?.NetAmount,
            transaction?.Currency,
            transaction?.InvoiceId,
            transaction?.PaypalReferenceId,
            order?.Id,
            order?.Status.ToString(),
            order?.Total(),
            state == ReconciliationMatchState.PayPalOnly ? null : order?.BuyerId,
            paymentSummary,
            transaction?.TransactionInitiationDate);
    }
}
