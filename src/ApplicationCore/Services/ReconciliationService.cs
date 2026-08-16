using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private const string InvoicePrefix = "ESHOP-";
    private const decimal Tolerance = 0.01m;

    private readonly IPayPalClient _payPal;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPayPalClient payPal, IReadRepository<Payment> paymentRepository)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> BuildAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new PaymentValidationException("'to' must be later than 'from'.");
        }

        // PayPal's own record for the whole range (chunked to its 31-day window and paged in full by the client).
        var transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);

        // The eShop side: captures that moved money in the range.
        var captured = await _paymentRepository.ListAsync(new CapturedPaymentsSpecification(), cancellationToken);
        var eShopByInvoice = captured
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId!)
            .ToDictionary(g => g.Key, g => g.First());

        var payPalInvoices = new HashSet<string>(
            transactions.Where(t => !string.IsNullOrEmpty(t.InvoiceId)).Select(t => t.InvoiceId!),
            StringComparer.OrdinalIgnoreCase);

        var rows = new List<ReconciliationRow>();
        var inPayPalNotInEShop = 0;
        var matched = 0;

        foreach (var t in transactions.OrderBy(t => t.InitiationDate))
        {
            var invoiceId = FirstReference(t.InvoiceId, t.CustomField);
            Payment? payment = null;
            if (invoiceId is not null)
            {
                eShopByInvoice.TryGetValue(invoiceId, out payment);
            }

            decimal? eShopAmount = payment?.CapturedAmount ?? payment?.Amount;
            var agree = payment is not null && eShopAmount is not null
                && Math.Abs(Math.Abs(t.Amount) - eShopAmount.Value) <= Tolerance;

            if (payment is not null)
            {
                matched++;
            }
            else if (invoiceId is not null && invoiceId.StartsWith(InvoicePrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Looks like one of ours, but eShop has no order for it — a genuine "PayPal knows, eShop doesn't".
                inPayPalNotInEShop++;
            }

            rows.Add(new ReconciliationRow(
                t.TransactionId, t.Status, t.EventCode, t.Amount, t.Fee, t.Currency, t.InitiationDate,
                invoiceId, payment?.OrderId, eShopAmount, agree));
        }

        // eShop captures in range that PayPal has not reported (usually reporting lag, not a discrepancy).
        var missing = captured
            .Where(p => p.CapturedAt is not null && p.CapturedAt >= from && p.CapturedAt <= to)
            .Where(p => string.IsNullOrEmpty(p.InvoiceId) || !payPalInvoices.Contains(p.InvoiceId!))
            .Select(p => new MissingInPayPalRow(p.OrderId, p.InvoiceId, p.CaptureId,
                p.CapturedAmount ?? p.Amount, p.Currency, p.CapturedAt))
            .OrderBy(m => m.OrderId)
            .ToList();

        var note =
            "PayPal transaction reporting lags live activity by up to a few hours, so captures created very " +
            "recently may not appear yet and will show under 'inEShopNotInPayPal' until PayPal catches up. " +
            "Transactions are listed for the whole range; 'matchedOrderId' is null for entries PayPal has that " +
            "eShop does not.";

        return new ReconciliationReport(
            from, to,
            PayPalTransactionCount: rows.Count,
            MatchedCount: matched,
            InPayPalNotInEShopCount: inPayPalNotInEShop,
            InEShopNotInPayPalCount: missing.Count,
            Transactions: rows,
            InEShopNotInPayPal: missing,
            Note: note);
    }

    private static string? FirstReference(string? invoiceId, string? customField)
    {
        if (!string.IsNullOrEmpty(invoiceId)) return invoiceId;
        if (!string.IsNullOrEmpty(customField)) return customField;
        return null;
    }
}
