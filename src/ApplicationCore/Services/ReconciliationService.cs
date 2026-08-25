using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class ReconciliationService : IReconciliationService
{
    private const string OrderReferenceType = "ODR";

    private readonly IPaymentGateway _gateway;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationService(IPaymentGateway gateway, IReadRepository<Payment> paymentRepository)
    {
        _gateway = gateway;
        _paymentRepository = paymentRepository;
    }

    public async Task<ReconciliationReport> BuildReportAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var localPayments = await _paymentRepository.ListAsync(new PaymentsInDateRangeSpecification(from, to), ct);

        var matched = new List<ReconciliationEntry>();
        var payPalOnly = new List<ReconciliationEntry>();
        var matchedLocalPaymentIds = new HashSet<int>();

        foreach (var txn in payPalTransactions)
        {
            var localMatch = localPayments.FirstOrDefault(p =>
                (txn.PayPalReferenceIdType == OrderReferenceType && p.PayPalOrderId == txn.PayPalReferenceId) ||
                (p.PayPalCaptureId is not null && p.PayPalCaptureId == txn.TransactionId));

            if (localMatch is not null)
            {
                matchedLocalPaymentIds.Add(localMatch.Id);
                matched.Add(new ReconciliationEntry(
                    txn.TransactionId,
                    localMatch.OrderId,
                    txn.Amount,
                    localMatch.CapturedAmount ?? localMatch.Amount,
                    txn.Currency ?? localMatch.Currency,
                    txn.Status,
                    "Matched between PayPal and eShop."));
            }
            else
            {
                payPalOnly.Add(new ReconciliationEntry(
                    txn.TransactionId,
                    null,
                    txn.Amount,
                    null,
                    txn.Currency,
                    txn.Status,
                    "PayPal reports this transaction; no matching eShop order/payment was found."));
            }
        }

        var eShopOnly = localPayments
            .Where(p => !matchedLocalPaymentIds.Contains(p.Id) && p.Status != PaymentStatus.AwaitingAuthorization)
            .Select(p => new ReconciliationEntry(
                p.PayPalCaptureId ?? p.PayPalAuthorizationId,
                p.OrderId,
                null,
                p.CapturedAmount ?? p.Amount,
                p.Currency,
                p.Status.ToString(),
                "eShop recorded this payment, but PayPal's transaction report does not show it for this range yet " +
                "(PayPal's reporting can lag live activity, or the range may not cover it)."))
            .ToList();

        return new ReconciliationReport(matched, payPalOnly, eShopOnly);
    }
}
