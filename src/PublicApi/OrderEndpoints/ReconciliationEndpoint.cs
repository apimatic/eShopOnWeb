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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range (ISO-8601
/// from/to, whole range — all pages) and lines them up against eShop orders, so a payment
/// PayPal knows about and eShop doesn't — or the reverse — is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentGateway, IRepository<Payment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentGateway paymentGateway,
                IRepository<Payment> paymentRepository) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), paymentGateway, paymentRepository);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentGateway paymentGateway,
        IRepository<Payment> paymentRepository)
    {
        if (request.To <= request.From)
        {
            throw new PaymentConflictException("The 'to' date-time must be after the 'from' date-time.");
        }

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To
        };

        var payPalTransactions = await paymentGateway.ListTransactionsAsync(request.From, request.To);
        var payments = await paymentRepository.ListAsync(new PaymentsWithRefundsSpecification());

        // Index every PayPal-owned id we know about, so either side can be lined up with the other.
        var localByPayPalId = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId is not null) localByPayPalId[payment.AuthorizationId] = payment;
            if (payment.CaptureId is not null) localByPayPalId[payment.CaptureId] = payment;
            foreach (var refund in payment.Refunds.Where(r => r.PayPalRefundId is not null))
            {
                localByPayPalId[refund.PayPalRefundId!] = payment;
            }
        }

        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var transaction in payPalTransactions)
        {
            var matched = localByPayPalId.TryGetValue(transaction.TransactionId, out var payment);
            if (matched)
            {
                matchedTransactionIds.Add(transaction.TransactionId);
            }

            response.Transactions.Add(new ReconciliationTransactionDto
            {
                TransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee,
                Time = transaction.Time,
                MatchedToEShop = matched,
                OrderId = payment?.OrderId,
                PaymentId = payment?.Id
            });
        }

        // Local payments created inside the window whose PayPal ids never showed up in the report.
        response.EShopPaymentsMissingFromPayPal = payments
            .Where(p => p.CreatedAt >= request.From && p.CreatedAt <= request.To)
            .Where(p => p.Status != PaymentStatus.Failed)
            .Where(p => !KnownIds(p).Any(matchedTransactionIds.Contains))
            .Select(p => new MissingPaymentDto
            {
                PaymentId = p.Id,
                OrderId = p.OrderId,
                Status = p.Status.ToString(),
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId
            })
            .ToList();

        response.Summary = new ReconciliationSummaryDto
        {
            PayPalTransactionCount = payPalTransactions.Count,
            MatchedCount = response.Transactions.Count(t => t.MatchedToEShop),
            UnmatchedPayPalCount = response.Transactions.Count(t => !t.MatchedToEShop),
            EShopPaymentsMissingFromPayPalCount = response.EShopPaymentsMissingFromPayPal.Count
        };

        return Results.Ok(response);
    }

    private static IEnumerable<string> KnownIds(Payment payment)
    {
        if (payment.AuthorizationId is not null) yield return payment.AuthorizationId;
        if (payment.CaptureId is not null) yield return payment.CaptureId;
        foreach (var id in payment.Refunds.Select(r => r.PayPalRefundId).Where(id => id is not null))
        {
            yield return id!;
        }
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<MissingPaymentDto> EShopPaymentsMissingFromPayPal { get; set; } = new();
    public ReconciliationSummaryDto Summary { get; set; } = new();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? Time { get; set; }
    public bool MatchedToEShop { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
}

public class MissingPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}

public class ReconciliationSummaryDto
{
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedPayPalCount { get; set; }
    public int EShopPaymentsMissingFromPayPalCount { get; set; }
}
