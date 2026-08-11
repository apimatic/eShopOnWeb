using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IReadRepository<Payment> _paymentRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentSettings _settings;

    public ReconciliationService(
        IReadRepository<Payment> paymentRepository,
        IPaymentGateway gateway,
        PaymentSettings settings)
    {
        _paymentRepository = paymentRepository;
        _gateway = gateway;
        _settings = settings;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new Exceptions.PaymentException("'to' must not be earlier than 'from'.", 400);
        }

        // eShop side: every capture and every refund that carries a PayPal id and falls in the range.
        var payments = await _paymentRepository.ListAsync(cancellationToken);
        var eshopTransactions = BuildEShopTransactions(payments, from, to);
        var eshopById = eshopTransactions
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        // PayPal side: its own record across the WHOLE range (all pages).
        var paypalTransactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var paypalIds = new HashSet<string>(paypalTransactions.Select(t => t.TransactionId));

        var matched = new List<ReconciliationMatch>();
        var inPayPalNotInEShop = new List<GatewayTransaction>();

        foreach (var pp in paypalTransactions)
        {
            if (eshopById.TryGetValue(pp.TransactionId, out var eshop))
            {
                var amountsAgree = pp.Amount is decimal a && Math.Abs(a) == eshop.Amount;
                matched.Add(new ReconciliationMatch(pp.TransactionId, eshop.OrderId, eshop.Kind,
                    pp.Amount, eshop.Amount, amountsAgree));
            }
            else
            {
                inPayPalNotInEShop.Add(pp);
            }
        }

        var inEShopNotInPayPal = eshopTransactions
            .Where(t => !paypalIds.Contains(t.TransactionId))
            .ToList();

        return new ReconciliationReport(
            from, to,
            paypalTransactions.Count,
            eshopTransactions.Count,
            matched,
            inPayPalNotInEShop,
            inEShopNotInPayPal);
    }

    private List<EShopTransaction> BuildEShopTransactions(IReadOnlyList<Payment> payments,
        DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<EShopTransaction>();

        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null && payment.CapturedAmount is decimal captured &&
                payment.CapturedAt is DateTimeOffset capturedAt && InRange(capturedAt, from, to))
            {
                result.Add(new EShopTransaction(payment.CaptureId, payment.OrderId, "Capture",
                    captured, payment.Currency, payment.CaptureStatus ?? "UNKNOWN"));
            }

            foreach (var refund in payment.Refunds)
            {
                if (InRange(refund.CreatedAt, from, to))
                {
                    result.Add(new EShopTransaction(refund.RefundId, payment.OrderId, "Refund",
                        refund.Amount, payment.Currency, refund.Status));
                }
            }
        }

        return result;
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;
}
