using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own transaction records up against eShop payments for a date range so that a
/// payment PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IPaymentGateway _paymentGateway;
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly ICurrencyProvider _currencyProvider;

    public ReconciliationService(
        IPaymentGateway paymentGateway,
        IReadRepository<Payment> paymentRepository,
        ICurrencyProvider currencyProvider)
    {
        _paymentGateway = paymentGateway;
        _paymentRepository = paymentRepository;
        _currencyProvider = currencyProvider;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var currency = _currencyProvider.CurrencyCode;

        // PayPal's record for the whole range (the gateway follows pagination internally).
        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's record: payments that actually reached PayPal within the range.
        var allPayments = await _paymentRepository.ListAsync(cancellationToken);
        var eShopPayments = allPayments
            .Where(p => !string.IsNullOrEmpty(p.PayPalOrderId))
            .Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .ToList();

        var matched = new List<ReconciliationEntry>();
        var missingInEShop = new List<ReconciliationEntry>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var payment = eShopPayments.FirstOrDefault(p => Matches(p, txn));
            if (payment is not null)
            {
                matchedPaymentIds.Add(payment.Id);
                matched.Add(new ReconciliationEntry(
                    "matched",
                    payment.OrderId,
                    payment.InvoiceId,
                    payment.PayPalOrderId,
                    txn.TransactionId,
                    payment.Status.ToString(),
                    txn.Status,
                    payment.Amount,
                    txn.Amount?.Amount,
                    txn.Amount?.CurrencyCode ?? currency));
            }
            else
            {
                missingInEShop.Add(new ReconciliationEntry(
                    "missing-in-eshop",
                    null,
                    txn.InvoiceId,
                    null,
                    txn.TransactionId,
                    null,
                    txn.Status,
                    null,
                    txn.Amount?.Amount,
                    txn.Amount?.CurrencyCode ?? currency));
            }
        }

        var missingInPayPal = eShopPayments
            .Where(p => !matchedPaymentIds.Contains(p.Id))
            .Select(p => new ReconciliationEntry(
                "missing-in-paypal",
                p.OrderId,
                p.InvoiceId,
                p.PayPalOrderId,
                null,
                p.Status.ToString(),
                null,
                p.Amount,
                null,
                p.CurrencyCode))
            .ToList();

        var note =
            "PayPal transaction reporting lags live activity, so payments created very recently may " +
            "not yet appear in PayPal's record and can surface here as 'missing-in-paypal' until the " +
            "reporting catches up. This is expected in the sandbox for a range covering just-created payments.";

        return new ReconciliationReport(
            from, to,
            transactions.Count,
            eShopPayments.Count,
            matched,
            missingInEShop,
            missingInPayPal,
            note);
    }

    private static bool Matches(Payment payment, PayPalTransaction txn)
    {
        // Correlate only on globally-unique keys. The invoice id (ESHOP-{run}-{order}) is unique per
        // run, and the authorization/capture/order ids are PayPal-owned and unique. The order id
        // alone is deliberately NOT used: the in-memory store resets it to 1 each run and it is not
        // unique across runs sharing a sandbox account, which would produce false matches.
        if (!string.IsNullOrEmpty(payment.InvoiceId) &&
            string.Equals(payment.InvoiceId, txn.InvoiceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IdMatches(txn.TransactionId, payment.PayPalOrderId)
            || IdMatches(txn.TransactionId, payment.AuthorizationId)
            || IdMatches(txn.TransactionId, payment.CaptureId);
    }

    private static bool IdMatches(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
