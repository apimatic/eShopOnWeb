using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPayPalClient _payPal;

    public ReconciliationService(IReadRepository<Payment> paymentRepository, IPayPalClient payPal)
    {
        _paymentRepository = paymentRepository;
        _payPal = payPal;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new PaymentException("The reconciliation 'to' date must not be earlier than the 'from' date.");

        // PayPal's own record over the whole range (the client chunks the range and pages internally).
        var payPalTransactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var payPalById = payPalTransactions
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop's record over the range: captures and refunds it has booked with a PayPal id.
        var payments = await _paymentRepository.ListAsync(new AllPaymentsSpecification(), cancellationToken);
        var eShopEntries = BuildEShopEntries(payments, from, to);
        var eShopIds = eShopEntries.Select(e => e.TransactionId).ToHashSet();

        var matched = new List<ReconciliationEntry>();
        var inEShopNotPayPal = new List<ReconciliationEntry>();
        foreach (var entry in eShopEntries)
        {
            if (payPalById.ContainsKey(entry.TransactionId))
                matched.Add(entry with { InPayPal = true, InEShop = true });
            else
                inEShopNotPayPal.Add(entry with { InPayPal = false, InEShop = true });
        }

        var inPayPalNotEShop = payPalTransactions
            .Where(t => !eShopIds.Contains(t.TransactionId))
            .Select(t => new ReconciliationEntry(t.TransactionId, KindFromEventCode(t.EventCode), t.Amount,
                t.CurrencyCode, OrderId: null, InPayPal: true, InEShop: false))
            .ToList();

        return new ReconciliationReport(from, to, matched, inPayPalNotEShop, inEShopNotPayPal);
    }

    private static List<ReconciliationEntry> BuildEShopEntries(IReadOnlyList<Payment> payments,
        DateTimeOffset from, DateTimeOffset to)
    {
        var entries = new List<ReconciliationEntry>();
        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null && payment.CapturedAt is { } capturedAt &&
                capturedAt >= from && capturedAt <= to)
            {
                entries.Add(new ReconciliationEntry(payment.CaptureId, "capture", payment.CapturedAmount ?? 0m,
                    payment.Currency, payment.OrderId, InPayPal: false, InEShop: true));
            }

            foreach (var refund in payment.Refunds)
            {
                if (refund.PayPalRefundId is not null && refund.CreatedAt >= from && refund.CreatedAt <= to)
                {
                    entries.Add(new ReconciliationEntry(refund.PayPalRefundId, "refund", refund.Amount,
                        payment.Currency, payment.OrderId, InPayPal: false, InEShop: true));
                }
            }
        }

        return entries;
    }

    private static string KindFromEventCode(string? eventCode)
    {
        if (string.IsNullOrEmpty(eventCode))
            return "unknown";
        // T11xx is PayPal's family of refund/reversal event codes; everything else here is a payment.
        return eventCode.StartsWith("T11", StringComparison.OrdinalIgnoreCase) ? "refund" : "payment";
    }
}
