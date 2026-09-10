using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private const string InvoicePrefix = "ESHOP-";

    private readonly IPayPalGateway _gateway;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPayPalGateway gateway, IReadRepository<Payment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        // eShop side: payments that actually moved money (were captured), keyed by their invoice id.
        var payments = await _paymentRepository.ListAsync(ct);
        var capturedByInvoice = payments
            .Where(p => p.CaptureId is not null)
            .ToDictionary(p => Invoice(p.OrderId), p => p);

        var lines = new List<ReconciliationLine>();
        var seenInvoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions)
        {
            var invoice = tx.CorrelationId;
            int? orderId = TryParseOrderId(invoice);

            if (invoice is not null && capturedByInvoice.TryGetValue(invoice, out var payment))
            {
                seenInvoices.Add(invoice);
                var eShopAmount = payment.CapturedAmount ?? payment.Amount;
                var amountsMatch = tx.Amount.HasValue && decimal.Round(tx.Amount.Value, 2) == decimal.Round(eShopAmount, 2);
                lines.Add(new ReconciliationLine(
                    amountsMatch ? "Matched" : "AmountMismatch",
                    invoice, payment.OrderId, tx.TransactionId, tx.Amount, eShopAmount, tx.Currency ?? payment.Currency, tx.Status));
            }
            else
            {
                // PayPal knows about it; eShop does not have a matching captured payment.
                lines.Add(new ReconciliationLine(
                    "MissingInEShop", invoice, orderId, tx.TransactionId, tx.Amount, null, tx.Currency, tx.Status));
            }
        }

        // eShop captured payments PayPal's report does not (yet) mention.
        foreach (var kvp in capturedByInvoice)
        {
            if (seenInvoices.Contains(kvp.Key))
                continue;
            var payment = kvp.Value;
            lines.Add(new ReconciliationLine(
                "MissingInPayPal", kvp.Key, payment.OrderId, null, null,
                payment.CapturedAmount ?? payment.Amount, payment.Currency, null));
        }

        return new ReconciliationReport(
            from,
            to,
            transactions.Count,
            capturedByInvoice.Count,
            lines.Count(l => l.MatchStatus == "Matched"),
            lines.Count(l => l.MatchStatus == "MissingInEShop"),
            lines.Count(l => l.MatchStatus == "MissingInPayPal"),
            lines.Count(l => l.MatchStatus == "AmountMismatch"),
            lines);
    }

    private static string Invoice(int orderId) => $"{InvoicePrefix}{orderId}";

    private static int? TryParseOrderId(string? invoice)
    {
        if (invoice is not null
            && invoice.StartsWith(InvoicePrefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(invoice.AsSpan(InvoicePrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return id;
        return null;
    }
}
