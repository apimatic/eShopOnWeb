using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public bool Matched { get; set; }
    public string? MatchType { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
}

public class UnmatchedPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<UnmatchedPaymentDto> EShopPaymentsMissingFromPayPal { get; set; } = new List<UnmatchedPaymentDto>();
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedPayPalTransactionCount { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and
/// lines them up against eShop orders/payments, surfacing discrepancies in both
/// directions. Covers the whole range (all pages, chunked into 31-day windows).
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from,
             DateTimeOffset to,
             IRepository<Payment> paymentRepository,
             IPayPalClient payPalClient) =>
            {
                return await HandleAsync(from, to, paymentRepository, payPalClient);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to,
        IRepository<Payment> paymentRepository, IPayPalClient payPalClient)
    {
        if (from == default || to == default || from >= to)
        {
            return Results.BadRequest(new { message = "Query parameters 'from' and 'to' are required ISO-8601 date-times, and 'from' must be before 'to'." });
        }

        var transactions = await payPalClient.SearchTransactionsAsync(from, to);
        var payments = await paymentRepository.ListAsync(new PaymentsWithRefundsSpec());

        var response = new ReconciliationResponse { From = from, To = to };

        foreach (var transaction in transactions)
        {
            var dto = new ReconciliationTransactionDto
            {
                TransactionId = transaction.TransactionId,
                ReferenceId = transaction.ReferenceId,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                FeeAmount = transaction.FeeAmount,
                InitiationDate = transaction.InitiationDate
            };

            var match = FindMatch(transaction, payments);
            if (match.Payment != null)
            {
                dto.Matched = true;
                dto.MatchType = match.MatchType;
                dto.PaymentId = match.Payment.Id;
                dto.OrderId = match.Payment.OrderId;
            }

            response.Transactions.Add(dto);
        }

        var payPalIds = new HashSet<string>(
            transactions.Select(t => t.TransactionId)
                .Concat(transactions.Where(t => t.ReferenceId != null).Select(t => t.ReferenceId!)),
            StringComparer.OrdinalIgnoreCase);

        response.EShopPaymentsMissingFromPayPal = payments
            .Where(p => p.AuthorizationId != null || p.CaptureId != null)
            .Where(p => !payPalIds.Contains(p.AuthorizationId!) && !payPalIds.Contains(p.CaptureId!) && !payPalIds.Contains(p.PayPalOrderId!))
            .Select(p => new UnmatchedPaymentDto
            {
                PaymentId = p.Id,
                OrderId = p.OrderId,
                Status = p.Status.ToString(),
                Amount = p.Amount,
                Currency = p.Currency,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId
            })
            .ToList();

        response.PayPalTransactionCount = response.Transactions.Count;
        response.MatchedCount = response.Transactions.Count(t => t.Matched);
        response.UnmatchedPayPalTransactionCount = response.Transactions.Count(t => !t.Matched);
        response.Note = "PayPal transaction reporting lags live activity (up to a few hours in sandbox); very recent payments may legitimately be absent from PayPal's list.";

        return Results.Ok(response);
    }

    private static (Payment? Payment, string? MatchType) FindMatch(PayPalTransactionInfo transaction, List<Payment> payments)
    {
        var byCapture = payments.FirstOrDefault(p => p.CaptureId == transaction.TransactionId);
        if (byCapture != null) return (byCapture, "capture");

        var byAuthorization = payments.FirstOrDefault(p => p.AuthorizationId == transaction.TransactionId);
        if (byAuthorization != null) return (byAuthorization, "authorization");

        var byRefund = payments.FirstOrDefault(p => p.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId));
        if (byRefund != null) return (byRefund, "refund");

        if (transaction.ReferenceId != null)
        {
            var byOrderId = payments.FirstOrDefault(p => p.PayPalOrderId == transaction.ReferenceId);
            if (byOrderId != null) return (byOrderId, "paypalOrderReference");
        }

        return (null, null);
    }
}
