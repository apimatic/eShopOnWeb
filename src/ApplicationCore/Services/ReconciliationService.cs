using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines up PayPal's own transaction records (over the whole date range, all pages) against eShop
/// payments, surfacing anything one side knows and the other doesn't.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(IReadRepository<Payment> paymentRepository, IPaymentGateway gateway, IAppLogger<ReconciliationService> logger)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to <= from)
            return Result<ReconciliationReport>.Invalid(ServiceResults.Validation("range", "'to' must be after 'from'."));

        IReadOnlyList<GatewayTransaction> transactions;
        try
        {
            transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        }
        catch (PaymentGatewayException ex)
        {
            return Result<ReconciliationReport>.Error(ex.Message);
        }

        var payments = await _paymentRepository.ListAsync(ct);
        var paymentByOrder = payments
            .Where(p => p.PayPalOrderId is not null)
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<ReconciliationPayPalOnly>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var tx in transactions)
        {
            var orderId = OrderCorrelation.TryParseOrderId(tx.InvoiceId);
            if (orderId is int oid && paymentByOrder.TryGetValue(oid, out var payment))
            {
                matchedOrderIds.Add(oid);
                matched.Add(new ReconciliationMatch(oid, payment.Status.ToString(), tx.TransactionId, tx.InvoiceId,
                    tx.Amount, tx.Status, tx.EventCode, tx.InitiationDate));
            }
            else
            {
                payPalOnly.Add(new ReconciliationPayPalOnly(tx.TransactionId, tx.InvoiceId, tx.Amount,
                    tx.CurrencyCode, tx.Status, tx.EventCode, tx.InitiationDate));
            }
        }

        // eShop payments whose activity falls in the range but that PayPal's report does not show
        // (expected in sandbox: PayPal's reporting lags recent activity).
        var eShopOnly = payments
            .Where(p => p.PayPalOrderId is not null && !matchedOrderIds.Contains(p.OrderId))
            .Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .Select(p => new ReconciliationEShopOnly(p.OrderId, p.Status.ToString(), p.PayPalOrderId,
                p.AuthorizationId, p.CaptureId, p.Amount, p.CurrencyCode))
            .ToList();

        _logger.LogInformation("Reconciliation {0}..{1}: {2} PayPal txns ({3} matched, {4} PayPal-only), {5} eShop-only.",
            from, to, transactions.Count, matched.Count, payPalOnly.Count, eShopOnly.Count);

        var report = new ReconciliationReport(from, to, matched, payPalOnly, eShopOnly);
        return Result<ReconciliationReport>.Success(report);
    }
}
