using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _gateway;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly PaymentSettings _settings;

    public ReconciliationService(
        IPaymentGateway gateway,
        IRepository<Payment> paymentRepository,
        IOptions<PaymentSettings> settings)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
        _settings = settings.Value;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new ArgumentException("The 'to' date-time must be after the 'from' date-time.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var localPayments = await _paymentRepository.ListAsync(
            new PaymentsInRangeSpecification(from, to), cancellationToken);

        // Every PayPal-owned id eShop knows about -> the local payment that recorded it.
        var knownIds = new Dictionary<string, (Payment Payment, string Role)>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in localPayments)
        {
            if (!string.IsNullOrEmpty(payment.PayPalOrderId))
                knownIds.TryAdd(payment.PayPalOrderId, (payment, "PayPal order"));
            if (!string.IsNullOrEmpty(payment.AuthorizationId))
                knownIds.TryAdd(payment.AuthorizationId, (payment, "authorization"));
            if (!string.IsNullOrEmpty(payment.CaptureId))
                knownIds.TryAdd(payment.CaptureId, (payment, "capture"));
            foreach (var refund in payment.Refunds)
                knownIds.TryAdd(refund.PayPalRefundId, (payment, "refund"));
        }

        var lines = new List<ReconciliationLine>();
        var seenTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions)
        {
            seenTransactionIds.Add(tx.TransactionId);
            if (!string.IsNullOrEmpty(tx.ReferenceId))
            {
                seenTransactionIds.Add(tx.ReferenceId);
            }

            var matched = MatchTransaction(tx, knownIds, out var payment, out var note);
            lines.Add(new ReconciliationLine(
                tx.TransactionId,
                tx.ReferenceId,
                tx.EventCode,
                tx.Status,
                tx.Amount,
                tx.Currency,
                tx.FeeAmount,
                tx.InitiationDate,
                matched ? payment!.OrderId : null,
                matched ? payment!.Id : null,
                note));
        }

        var missing = new List<ReconciliationUnmatchedLocal>();
        foreach (var payment in localPayments)
        {
            var localIds = new[] { payment.CaptureId, payment.AuthorizationId, payment.PayPalOrderId }
                .Concat(payment.Refunds.Select(r => r.PayPalRefundId))
                .Where(id => !string.IsNullOrEmpty(id))
                .Cast<string>()
                .ToList();

            if (localIds.Count > 0 && !localIds.Any(seenTransactionIds.Contains))
            {
                missing.Add(new ReconciliationUnmatchedLocal(
                    payment.Id,
                    payment.OrderId,
                    payment.PayPalOrderId,
                    payment.AuthorizationId,
                    payment.CaptureId,
                    payment.Status.ToString(),
                    payment.CapturedAmount ?? payment.Amount,
                    payment.Currency,
                    payment.CreatedAt,
                    "Recorded in eShop but absent from PayPal's transaction report for this range " +
                    "(PayPal sandbox reporting lags live activity; re-run later before treating as a discrepancy)."));
            }
        }

        return new ReconciliationReport(from, to, _settings.Currency, lines, missing);
    }

    private static bool MatchTransaction(
        GatewayTransaction tx,
        Dictionary<string, (Payment Payment, string Role)> knownIds,
        out Payment? payment,
        out string note)
    {
        if (knownIds.TryGetValue(tx.TransactionId, out var hit))
        {
            payment = hit.Payment;
            note = $"Matched eShop order {hit.Payment.OrderId} via {hit.Role} id.";
            return true;
        }
        if (!string.IsNullOrEmpty(tx.ReferenceId) && knownIds.TryGetValue(tx.ReferenceId, out hit))
        {
            payment = hit.Payment;
            note = $"Matched eShop order {hit.Payment.OrderId} via referenced {hit.Role} id.";
            return true;
        }

        payment = null;
        note = "No matching eShop payment; PayPal knows about this transaction but eShop does not.";
        return false;
    }
}
