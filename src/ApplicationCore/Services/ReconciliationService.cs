using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using static Microsoft.eShopWeb.ApplicationCore.Services.ServiceResults;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IReadRepository<Order> orderRepository,
        IPayPalGateway payPalGateway,
        PayPalSettings settings,
        IAppLogger<ReconciliationService> logger)
    {
        _orderRepository = orderRepository;
        _payPalGateway = payPalGateway;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return Invalid<ReconciliationReport>("'to' must be on or after 'from'.");
        }

        // PayPal's own record for the range (chunked + fully paged inside the gateway).
        IReadOnlyList<PayPalTransaction> transactions;
        try
        {
            transactions = await _payPalGateway.ListTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Reconciliation query failed: {0} (debug id {1}).", ex.Message, ex.DebugId ?? "n/a");
            return Result<ReconciliationReport>.Error($"PayPal reporting query failed: {ex.Message}");
        }

        // eShop's own record for the range.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpec(from, to), cancellationToken);

        var matched = new List<ReconciliationLine>();
        var onlyInPayPal = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var order = FindMatchingOrder(txn, orders);
            if (order is not null)
            {
                matchedOrderIds.Add(order.Id);
                matched.Add(new ReconciliationLine(
                    MatchState: "Matched",
                    EshopOrderId: order.Id,
                    PayPalTransactionId: txn.TransactionId,
                    PayPalOrderId: order.Payment?.PayPalOrderId,
                    CaptureId: order.Payment?.CaptureId,
                    InvoiceId: txn.InvoiceId ?? order.Payment?.InvoiceId,
                    EshopAmount: order.Payment?.CapturedAmount ?? order.Total(),
                    PayPalAmount: txn.Amount,
                    PayPalStatus: txn.Status,
                    Date: txn.Date));
            }
            else
            {
                onlyInPayPal.Add(new ReconciliationLine(
                    MatchState: "OnlyInPayPal",
                    EshopOrderId: null,
                    PayPalTransactionId: txn.TransactionId,
                    PayPalOrderId: null,
                    CaptureId: null,
                    InvoiceId: txn.InvoiceId,
                    EshopAmount: null,
                    PayPalAmount: txn.Amount,
                    PayPalStatus: txn.Status,
                    Date: txn.Date));
            }
        }

        var onlyInEshop = orders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationLine(
                MatchState: "OnlyInEshop",
                EshopOrderId: o.Id,
                PayPalTransactionId: null,
                PayPalOrderId: o.Payment?.PayPalOrderId,
                CaptureId: o.Payment?.CaptureId,
                InvoiceId: o.Payment?.InvoiceId,
                EshopAmount: o.Payment?.CapturedAmount ?? o.Total(),
                PayPalAmount: null,
                PayPalStatus: o.Payment?.Status.ToString(),
                Date: o.OrderDate))
            .ToList();

        _logger.LogInformation("Reconciliation {0}..{1}: {2} PayPal txns, {3} eShop orders, {4} matched, {5} PayPal-only, {6} eShop-only.",
            from, to, transactions.Count, orders.Count, matched.Count, onlyInPayPal.Count, onlyInEshop.Count);

        var report = new ReconciliationReport(from, to, _settings.CurrencyCode, matched, onlyInPayPal, onlyInEshop);
        return Result<ReconciliationReport>.Success(report);
    }

    /// <summary>
    /// Matches a PayPal transaction to an eShop order using only strong, unique keys: the per-order
    /// invoice id (also echoed in custom_field), or PayPal's capture / authorization / order id
    /// surfacing as the reported transaction id. The bare order id is deliberately not used — it is a
    /// small, reused integer that collides with unrelated transactions on a shared account.
    /// </summary>
    private static Order? FindMatchingOrder(PayPalTransaction txn, IReadOnlyList<Order> orders)
    {
        if (!string.IsNullOrWhiteSpace(txn.InvoiceId))
        {
            var byInvoice = orders.FirstOrDefault(o => o.Payment?.InvoiceId == txn.InvoiceId);
            if (byInvoice is not null)
            {
                return byInvoice;
            }
        }

        // custom_field carries the same unique invoice id.
        if (!string.IsNullOrWhiteSpace(txn.CustomField))
        {
            var byCustom = orders.FirstOrDefault(o => o.Payment?.InvoiceId == txn.CustomField);
            if (byCustom is not null)
            {
                return byCustom;
            }
        }

        // The capture / authorization / order id can surface as the reported transaction id.
        return orders.FirstOrDefault(o =>
            o.Payment is not null &&
            (o.Payment.CaptureId == txn.TransactionId
             || o.Payment.AuthorizationId == txn.TransactionId
             || o.Payment.PayPalOrderId == txn.TransactionId));
    }
}
