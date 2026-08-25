using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private readonly IPayPalGateway _payPalGateway;
    private readonly IReadRepository<Entities.PaymentAggregate.Payment> _paymentRepository;

    public ReconciliationService(IPayPalGateway payPalGateway, IReadRepository<Entities.PaymentAggregate.Payment> paymentRepository)
    {
        _payPalGateway = payPalGateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from > to)
        {
            throw new ArgumentException("'from' must not be after 'to'.", nameof(from));
        }

        var payPalTransactions = await _payPalGateway.SearchTransactionsAsync(from, to, ct);
        var localPayments = await _paymentRepository.ListAsync(new PaymentsCreatedInRangeSpecification(from, to), ct);

        var localByPayPalOrderId = localPayments
            .Where(p => p.PayPalOrderId is not null)
            .ToLookup(p => p.PayPalOrderId!);

        var matchedPayPalOrderIds = new HashSet<string>();
        var entries = new List<ReconciliationEntry>();

        foreach (var txn in payPalTransactions)
        {
            var match = txn.PayPalOrderId is not null ? localByPayPalOrderId[txn.PayPalOrderId].FirstOrDefault() : null;
            if (match is not null)
            {
                matchedPayPalOrderIds.Add(txn.PayPalOrderId!);
                entries.Add(new ReconciliationEntry
                {
                    MatchStatus = ReconciliationMatchStatus.Matched,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalOrderId = txn.PayPalOrderId,
                    OrderId = match.OrderId,
                    PayPalAmount = txn.Amount,
                    EShopAmount = match.CapturedAmount ?? match.Amount,
                    PayPalStatus = txn.Status,
                    EShopStatus = match.Status.ToString()
                });
            }
            else
            {
                entries.Add(new ReconciliationEntry
                {
                    MatchStatus = ReconciliationMatchStatus.PayPalOnly,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalOrderId = txn.PayPalOrderId,
                    PayPalAmount = txn.Amount,
                    PayPalStatus = txn.Status
                });
            }
        }

        foreach (var payment in localPayments)
        {
            if (payment.PayPalOrderId is null || matchedPayPalOrderIds.Contains(payment.PayPalOrderId))
            {
                continue;
            }

            entries.Add(new ReconciliationEntry
            {
                MatchStatus = ReconciliationMatchStatus.EShopOnly,
                PayPalOrderId = payment.PayPalOrderId,
                OrderId = payment.OrderId,
                EShopAmount = payment.CapturedAmount ?? payment.Amount,
                EShopStatus = payment.Status.ToString()
            });
        }

        return new ReconciliationReport { From = from, To = to, Entries = entries };
    }
}
