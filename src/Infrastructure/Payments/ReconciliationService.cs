using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Payments.Dto;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Pulls PayPal's own record of transactions for a date range (Transaction Search API v1,
/// walking every page) and lines each one up against the payments eShop recorded locally.
/// </summary>
public class ReconciliationService : IReconciliationService
{
    private const int PageSize = 100;

    private readonly IPayPalClient _payPalClient;
    private readonly IReadRepository<OrderPayment> _paymentRepository;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPayPalClient payPalClient,
        IReadRepository<OrderPayment> paymentRepository,
        ILogger<ReconciliationService> logger)
    {
        _payPalClient = payPalClient;
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var transactions = await GetAllTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsWithRefundsSpec(), cancellationToken);

        // Index every PayPal-owned id eShop knows about.
        var byAuthorization = payments
            .Where(p => p.AuthorizationId != null)
            .ToDictionary(p => p.AuthorizationId!, p => p, StringComparer.OrdinalIgnoreCase);
        var byCapture = payments
            .Where(p => p.CaptureId != null)
            .ToDictionary(p => p.CaptureId!, p => p, StringComparer.OrdinalIgnoreCase);
        var byRefund = payments
            .SelectMany(p => p.Refunds.Select(r => new { Payment = p, Refund = r }))
            .ToDictionary(x => x.Refund.PayPalRefundId, x => x.Payment, StringComparer.OrdinalIgnoreCase);

        var report = new ReconciliationReport { From = from, To = to };
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in transactions)
        {
            var info = detail.TransactionInfo;
            var entry = new ReconciliationTransaction
            {
                TransactionId = info?.TransactionId,
                EventCode = info?.TransactionEventCode,
                Status = info?.TransactionStatus,
                Amount = info?.TransactionAmount?.Value,
                Currency = info?.TransactionAmount?.CurrencyCode,
                Fee = info?.FeeAmount?.Value,
                InitiationDate = info?.TransactionInitiationDate,
                ReferenceId = info?.PayPalReferenceId,
                InvoiceId = info?.InvoiceId
            };

            if (info?.TransactionId != null)
            {
                seenIds.Add(info.TransactionId);

                if (byAuthorization.TryGetValue(info.TransactionId, out var byAuth))
                {
                    entry.OrderId = byAuth.OrderId;
                    entry.MatchedWith = "authorization";
                }
                else if (byCapture.TryGetValue(info.TransactionId, out var byCap))
                {
                    entry.OrderId = byCap.OrderId;
                    entry.MatchedWith = "capture";
                }
                else if (byRefund.TryGetValue(info.TransactionId, out var byRef))
                {
                    entry.OrderId = byRef.OrderId;
                    entry.MatchedWith = "refund";
                }
                else
                {
                    report.MissingInEShop.Add(entry);
                }
            }

            report.Transactions.Add(entry);
        }

        // Local payment activity inside the range that PayPal did not report.
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId != null && InRange(payment.AuthorizedAt, from, to)
                && !seenIds.Contains(payment.AuthorizationId))
            {
                report.MissingInPayPal.Add(new ReconciliationLocalRecord
                {
                    OrderId = payment.OrderId,
                    RecordType = "authorization",
                    ProcessorId = payment.AuthorizationId,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    When = payment.AuthorizedAt
                });
            }

            if (payment.CaptureId != null && InRange(payment.CapturedAt, from, to)
                && !seenIds.Contains(payment.CaptureId))
            {
                report.MissingInPayPal.Add(new ReconciliationLocalRecord
                {
                    OrderId = payment.OrderId,
                    RecordType = "capture",
                    ProcessorId = payment.CaptureId,
                    Amount = payment.CapturedAmount ?? payment.Amount,
                    Currency = payment.Currency,
                    When = payment.CapturedAt
                });
            }

            foreach (var refund in payment.Refunds)
            {
                if (InRange(refund.CreatedAt, from, to) && !seenIds.Contains(refund.PayPalRefundId))
                {
                    report.MissingInPayPal.Add(new ReconciliationLocalRecord
                    {
                        OrderId = payment.OrderId,
                        RecordType = "refund",
                        ProcessorId = refund.PayPalRefundId,
                        Amount = refund.Amount,
                        Currency = payment.Currency,
                        When = refund.CreatedAt
                    });
                }
            }
        }

        return report;
    }

    private async Task<List<PayPalTransactionDetail>> GetAllTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var all = new List<PayPalTransactionDetail>();
        var page = 1;
        while (true)
        {
            PayPalTransactionSearchResponse response;
            try
            {
                response = await _payPalClient.SearchTransactionsAsync(from, to, page, PageSize, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentException(
                    $"PayPal could not provide the transaction report: {ex.Message} " +
                    $"(error {ex.ErrorName ?? ex.StatusCode.ToString()}, debug id {ex.DebugId}).", ex);
            }
            if (response.TransactionDetails != null)
            {
                all.AddRange(response.TransactionDetails);
            }

            var totalPages = response.TotalPages ?? 1;
            _logger.LogInformation("Reconciliation: fetched page {Page} of {TotalPages}.", page, totalPages);

            if (page >= totalPages || response.TransactionDetails == null || response.TransactionDetails.Count == 0)
            {
                break;
            }
            page++;
        }
        return all;
    }

    private static bool InRange(DateTimeOffset? when, DateTimeOffset from, DateTimeOffset to)
        => when.HasValue && when.Value >= from && when.Value <= to;
}
