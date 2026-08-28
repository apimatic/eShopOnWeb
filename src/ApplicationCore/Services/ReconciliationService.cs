using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Lines PayPal's own record of transactions up against eShop's payments for a date range, so a
/// payment PayPal knows about and eShop does not — or the reverse — becomes visible.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IReadRepository<Payment> paymentRepository,
        IPaymentGateway gateway,
        IAppLogger<ReconciliationService> logger)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw new PaymentValidationException("The 'to' date-time must be later than the 'from' date-time.");
        }

        var page = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new PaymentsOverlappingRangeSpecification(from, to), cancellationToken);

        // Every processor-side id we know about, mapped back to the payment that owns it. A single
        // eShop payment can appear under several ids (its order, hold, capture and each refund).
        var byProviderId = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        var byInvoiceId = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);

        foreach (var payment in payments)
        {
            Index(byProviderId, payment.PayPalOrderId, payment);
            Index(byProviderId, payment.AuthorizationId, payment);
            Index(byProviderId, payment.CaptureId, payment);
            foreach (var refund in payment.Refunds)
            {
                Index(byProviderId, refund.PayPalRefundId, payment);
            }

            Index(byInvoiceId, payment.InvoiceId, payment);
        }

        var matched = new List<ReconciliationMatch>();
        var onlyAtPayPal = new List<GatewayTransaction>();
        var seenPaymentIds = new HashSet<int>();

        foreach (var transaction in page.Transactions)
        {
            var (payment, matchedOn) = Match(transaction, byProviderId, byInvoiceId);

            if (payment is null)
            {
                onlyAtPayPal.Add(transaction);
                continue;
            }

            seenPaymentIds.Add(payment.Id);

            // The processor reports a refund as a negative amount, so compare magnitudes.
            var eShopAmount = ExpectedAmountFor(payment, transaction);
            var agree = transaction.Amount is not null && eShopAmount is not null &&
                        Math.Abs(Math.Abs(transaction.Amount.Value) - Math.Abs(eShopAmount.Value)) < 0.005m;

            matched.Add(new ReconciliationMatch(
                transaction.TransactionId,
                matchedOn,
                payment.OrderId,
                payment.Id,
                payment.Status.ToString(),
                transaction.Amount,
                eShopAmount,
                agree,
                transaction.Status,
                transaction.InitiatedAt));
        }

        // Payments where money actually moved but the processor's reporting shows nothing. In the
        // sandbox this is routinely just reporting lag, which the note says out loud.
        var onlyInEShop = payments
            .Where(p => !seenPaymentIds.Contains(p.Id))
            .Where(p => p.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded || p.AwaitingReconciliation)
            .Select(p => new ReconciliationUnmatchedPayment(
                p.OrderId,
                p.Id,
                p.Status.ToString(),
                p.PayPalOrderId,
                p.AuthorizationId,
                p.CaptureId,
                p.Amount,
                p.CapturedAmount,
                p.CurrencyCode,
                p.AwaitingReconciliation,
                p.AwaitingReconciliation
                    ? "This payment has an unsettled outcome. Check the processor for the ids above and clear it manually."
                    : "eShop recorded this payment but the processor's reporting does not show it for this range. " +
                      "Transaction reporting lags live activity, so a recent payment may simply not have appeared yet."))
            .ToList();

        _logger.LogInformation(
            $"Reconciliation {from:u}..{to:u}: {page.Transactions.Count} provider transaction(s), " +
            $"{matched.Count} matched, {onlyAtPayPal.Count} only at the processor, {onlyInEShop.Count} only in eShop.");

        return new ReconciliationReport(
            from,
            to,
            _gateway.CurrencyCode,
            page.LastRefreshedAt,
            page.Transactions.Count,
            matched.Count,
            onlyAtPayPal.Count,
            onlyInEShop.Count,
            matched,
            onlyAtPayPal,
            onlyInEShop);
    }

    private static (Payment? Payment, string MatchedOn) Match(
        GatewayTransaction transaction,
        IReadOnlyDictionary<string, Payment> byProviderId,
        IReadOnlyDictionary<string, Payment> byInvoiceId)
    {
        if (!string.IsNullOrEmpty(transaction.TransactionId) &&
            byProviderId.TryGetValue(transaction.TransactionId, out var byId))
        {
            return (byId, "transaction id");
        }

        if (!string.IsNullOrEmpty(transaction.InvoiceId) &&
            byInvoiceId.TryGetValue(transaction.InvoiceId!, out var byInvoice))
        {
            return (byInvoice, "invoice id");
        }

        // We stamp our own order reference on the purchase unit, which the processor echoes back in
        // reporting as the custom field — the last resort when neither id lines up.
        if (!string.IsNullOrEmpty(transaction.CustomField) &&
            byInvoiceId.TryGetValue(transaction.CustomField!, out var byCustom))
        {
            return (byCustom, "custom field");
        }

        return (null, "unmatched");
    }

    /// <summary>
    /// What eShop believes this particular transaction should be worth: the refund amount if the
    /// transaction is one of our refunds, otherwise the captured (or held) amount.
    /// </summary>
    private static decimal? ExpectedAmountFor(Payment payment, GatewayTransaction transaction)
    {
        var refund = payment.Refunds.FirstOrDefault(r =>
            string.Equals(r.PayPalRefundId, transaction.TransactionId, StringComparison.OrdinalIgnoreCase));

        return refund?.Amount ?? payment.CapturedAmount ?? payment.Amount;
    }

    private static void Index(IDictionary<string, Payment> index, string? key, Payment payment)
    {
        if (!string.IsNullOrEmpty(key))
        {
            index[key!] = payment;
        }
    }
}
