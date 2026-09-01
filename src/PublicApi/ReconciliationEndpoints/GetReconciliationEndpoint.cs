using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator: list PayPal's own record of transactions over a date range, lined up against
/// eShop orders/payments. Covers the whole range (chunked and paged), not just the first page.
/// Note: PayPal reporting lags live activity (up to ~3 hours), so very recent payments may
/// legitimately be absent from PayPal's side of the report.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IPayPalClient payPalClient,
                IRepository<Payment> paymentRepository, CancellationToken cancellationToken) =>
            {
                if (from is null || to is null)
                {
                    throw new PaymentException("Both 'from' and 'to' query parameters (ISO-8601 date-times) are required.");
                }

                if (to <= from)
                {
                    throw new PaymentException("'to' must be later than 'from'.");
                }

                var transactions = await payPalClient.ListTransactionsAsync(from.Value, to.Value, cancellationToken);
                var payments = await paymentRepository.ListAsync(new AllPaymentsWithRefundsSpec(), cancellationToken);

                var response = BuildReport(from.Value, to.Value, transactions, payments);
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    private static ReconciliationResponse BuildReport(DateTimeOffset from, DateTimeOffset to,
        IReadOnlyList<PayPalTransaction> transactions, IReadOnlyList<Payment> payments)
    {
        // Index every PayPal-owned id we know about, so a later report can line transactions up.
        var paymentsByPayPalId = new Dictionary<string, Payment>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            foreach (var id in EnumeratePayPalIds(payment))
            {
                paymentsByPayPalId.TryAdd(id, payment);
            }
        }

        var matchedPaymentIds = new HashSet<int>();
        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        foreach (var transaction in transactions.OrderBy(t => t.InitiatedAt))
        {
            var row = new ReconciliationTransactionDto
            {
                TransactionId = transaction.TransactionId,
                ReferenceId = transaction.ReferenceId,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee,
                InitiatedAt = transaction.InitiatedAt,
                InvoiceId = transaction.InvoiceId,
                CustomField = transaction.CustomField
            };

            var match = MatchPayment(transaction, paymentsByPayPalId);
            if (match is not null)
            {
                row.OrderId = match.OrderId;
                row.PaymentId = match.Id;
                matchedPaymentIds.Add(match.Id);
            }
            else
            {
                response.UnmatchedPayPalTransactions.Add(row);
            }

            response.Transactions.Add(row);
        }

        foreach (var payment in payments.Where(p => !matchedPaymentIds.Contains(p.Id)))
        {
            response.EShopPaymentsNotReportedByPayPal.Add(new ReconciliationPaymentDto
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                Status = payment.Status.ToString(),
                Currency = payment.Currency,
                AuthorizedAmount = payment.AuthorizedAmount,
                CapturedAmount = payment.CapturedAmount,
                TotalRefunded = payment.TotalRefunded,
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId
            });
        }

        response.Summary = new ReconciliationSummaryDto
        {
            PayPalTransactionCount = response.Transactions.Count,
            MatchedTransactionCount = response.Transactions.Count(t => t.PaymentId is not null),
            UnmatchedPayPalTransactionCount = response.UnmatchedPayPalTransactions.Count,
            EShopPaymentCount = payments.Count,
            EShopPaymentsNotReportedByPayPalCount = response.EShopPaymentsNotReportedByPayPal.Count
        };

        return response;
    }

    private static Payment? MatchPayment(PayPalTransaction transaction,
        Dictionary<string, Payment> paymentsByPayPalId)
    {
        // Exact matches only: on the PayPal-owned ids we stored, on the unique invoice id we
        // sent, and on the custom field. (The sandbox account may be shared, so fuzzy matching
        // on generic order-id patterns would cross-match other integrations' transactions.)
        foreach (var candidate in new[]
        {
            transaction.TransactionId, transaction.ReferenceId,
            transaction.InvoiceId, transaction.CustomField
        })
        {
            if (!string.IsNullOrEmpty(candidate)
                && paymentsByPayPalId.TryGetValue(candidate, out var match))
            {
                return match;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePayPalIds(Payment payment)
    {
        yield return payment.PayPalOrderId;
        yield return payment.AuthorizationId;
        yield return payment.InvoiceId;
        yield return payment.InvoiceId + "-capture";
        if (payment.CaptureId is not null)
        {
            yield return payment.CaptureId;
        }

        foreach (var refund in payment.Refunds)
        {
            yield return refund.PayPalRefundId;
        }
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public ReconciliationSummaryDto Summary { get; set; } = new ReconciliationSummaryDto();
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<ReconciliationTransactionDto> UnmatchedPayPalTransactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<ReconciliationPaymentDto> EShopPaymentsNotReportedByPayPal { get; set; } = new List<ReconciliationPaymentDto>();
}

public class ReconciliationSummaryDto
{
    public int PayPalTransactionCount { get; set; }
    public int MatchedTransactionCount { get; set; }
    public int UnmatchedPayPalTransactionCount { get; set; }
    public int EShopPaymentCount { get; set; }
    public int EShopPaymentsNotReportedByPayPalCount { get; set; }
}

public class ReconciliationTransactionDto
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiatedAt { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
}

public class ReconciliationPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal TotalRefunded { get; set; }
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
}
