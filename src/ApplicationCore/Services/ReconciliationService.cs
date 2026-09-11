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
    private readonly IPaymentGateway _gateway;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPaymentGateway gateway,
        IReadRepository<Order> orderRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _gateway = gateway;
        _orderRepository = orderRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentOperationException("The reconciliation 'to' date must not be earlier than 'from'.");
        }

        // Provider side: its own record of transactions across the whole range (all pages, chunked).
        var providerTransactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        // eShop side: every order that carries a payment, keyed by the exact reference sent to the
        // provider (which includes the run id, so a prior run's transactions never false-match).
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpec(), ct);
        var ordersByReference = orders
            .Where(o => o.Payment is not null)
            .ToDictionary(o => o.Payment!.ProviderReference, o => o);

        var matched = new List<ReconciliationLine>();
        var providerOnly = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in providerTransactions)
        {
            var reference = !string.IsNullOrEmpty(txn.InvoiceId) && ordersByReference.ContainsKey(txn.InvoiceId!)
                ? txn.InvoiceId!
                : txn.CustomField;

            if (reference is not null && ordersByReference.TryGetValue(reference, out var order))
            {
                matchedOrderIds.Add(order.Id);
                matched.Add(BuildLine(txn, order));
            }
            else
            {
                // The provider knows about this payment but eShop has no matching order.
                providerOnly.Add(BuildLine(txn, order: null,
                    parsedOrderId: PaymentReference.TryParseOrderId(txn.InvoiceId) ?? PaymentReference.TryParseOrderId(txn.CustomField)));
            }
        }

        // eShop side that the provider's report does not (yet) show. Only orders whose money was
        // actually captured within the range should appear in a provider transaction report; a
        // captured order missing from the report is a genuine gap (or reporting lag in sandbox).
        var eShopOnly = orders
            .Where(o => o.Payment?.CaptureId is not null
                        && o.Payment.CapturedAt.HasValue
                        && o.Payment.CapturedAt.Value >= from
                        && o.Payment.CapturedAt.Value <= to
                        && !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationLine(
                OrderReference: PaymentReference.For(o.Id),
                OrderId: o.Id,
                ProviderTransactionId: null,
                ProviderEventCode: null,
                ProviderStatus: null,
                ProviderAmount: null,
                ProviderCurrency: null,
                ProviderDate: null,
                EShopStatus: o.Status.ToString(),
                EShopCapturedAmount: o.Payment!.CapturedAmount,
                EShopCaptureId: o.Payment.CaptureId))
            .ToList();

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {providerTransactions.Count} provider txns, " +
            $"{matched.Count} matched, {providerOnly.Count} provider-only, {eShopOnly.Count} eshop-only.");

        return new ReconciliationReport(
            from, to,
            providerTransactions.Count,
            matched.Count,
            matched,
            providerOnly,
            eShopOnly);
    }

    private static ReconciliationLine BuildLine(GatewayTransaction txn, Order? order, int? parsedOrderId = null)
    {
        return new ReconciliationLine(
            OrderReference: txn.InvoiceId ?? txn.CustomField,
            OrderId: order?.Id ?? parsedOrderId,
            ProviderTransactionId: txn.TransactionId,
            ProviderEventCode: txn.EventCode,
            ProviderStatus: txn.Status,
            ProviderAmount: txn.Amount,
            ProviderCurrency: txn.CurrencyCode,
            ProviderDate: txn.InitiationDate,
            EShopStatus: order?.Status.ToString(),
            EShopCapturedAmount: order?.Payment?.CapturedAmount,
            EShopCaptureId: order?.Payment?.CaptureId);
    }
}
