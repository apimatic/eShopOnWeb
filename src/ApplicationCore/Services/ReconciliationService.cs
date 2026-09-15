using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPal;
    private readonly IReadRepository<OrderPayment> _paymentRepository;
    private readonly IAppLogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalGateway payPal,
        IReadRepository<OrderPayment> paymentRepository,
        IAppLogger<ReconciliationService> logger)
    {
        _payPal = payPal;
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The reconciliation 'to' date must not be earlier than the 'from' date.");
        }

        // PayPal's own record across the whole range (the gateway chunks and pages internally).
        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);

        // eShop's own record for the same window.
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsInRangeSpec(from, to), cancellationToken);

        // Index eShop payments by the invoice id we sent to PayPal (the reconciliation key) and by order id.
        var byInvoice = payments.Where(p => p.InvoiceId is not null)
            .ToDictionary(p => p.InvoiceId!, StringComparer.OrdinalIgnoreCase);
        var byOrderId = payments.ToDictionary(p => p.OrderId.ToString());

        var lines = new List<ReconciliationLine>();
        var matchedPayments = new HashSet<int>();
        var matchedLines = 0;
        var missingInEShop = 0;

        foreach (var tx in transactions)
        {
            var payment = ResolvePayment(tx, byInvoice, byOrderId);
            if (payment is not null)
            {
                matchedPayments.Add(payment.OrderId);
                matchedLines++;
                lines.Add(new ReconciliationLine(
                    ReconciliationMatch.Matched,
                    payment.InvoiceId,
                    payment.OrderId,
                    payment.Status,
                    payment.Amount,
                    tx.TransactionId,
                    tx.EventCode,
                    tx.Amount,
                    tx.Status,
                    tx.InitiationDate));
            }
            else
            {
                missingInEShop++;
                lines.Add(new ReconciliationLine(
                    ReconciliationMatch.MissingInEShop,
                    tx.InvoiceId,
                    null,
                    null,
                    null,
                    tx.TransactionId,
                    tx.EventCode,
                    tx.Amount,
                    tx.Status,
                    tx.InitiationDate));
            }
        }

        var missingInPayPal = 0;
        foreach (var payment in payments.Where(p => !matchedPayments.Contains(p.OrderId)))
        {
            missingInPayPal++;
            lines.Add(new ReconciliationLine(
                ReconciliationMatch.MissingInPayPal,
                payment.InvoiceId,
                payment.OrderId,
                payment.Status,
                payment.Amount,
                null,
                null,
                null,
                null,
                null));
        }

        _logger.LogInformation(
            $"Reconciliation {from:o}..{to:o}: {transactions.Count} PayPal tx, {payments.Count} eShop payments, " +
            $"{matchedLines} matched, {missingInEShop} missing-in-eShop, {missingInPayPal} missing-in-PayPal.");

        return new ReconciliationReport(from, to, transactions.Count, payments.Count,
            matchedLines, missingInEShop, missingInPayPal, lines);
    }

    private static OrderPayment? ResolvePayment(PayPalTransaction tx,
        IReadOnlyDictionary<string, OrderPayment> byInvoice,
        IReadOnlyDictionary<string, OrderPayment> byOrderId)
    {
        if (tx.InvoiceId is not null && byInvoice.TryGetValue(tx.InvoiceId, out var byInv))
        {
            return byInv;
        }
        // custom_field carries the eShop order id as a fallback reconciliation key.
        if (tx.CustomField is not null && byOrderId.TryGetValue(tx.CustomField, out var byCustom))
        {
            return byCustom;
        }
        return null;
    }
}
