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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }

    /// <summary>The eShop order this PayPal transaction lines up with, if any.</summary>
    public int? MatchedOrderId { get; set; }

    /// <summary>Which stored PayPal id matched: AuthorizationId, CaptureId, RefundId or PayPalOrderId.</summary>
    public string? MatchedBy { get; set; }
}

public class ReconciliationEShopPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PayPalOrderId { get; set; } = string.Empty;
    public string AuthorizationId { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public List<string> RefundIds { get; set; } = new();
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new();
    public List<ReconciliationTransactionDto> UnmatchedPayPalTransactions { get; set; } = new();
    public List<ReconciliationEShopPaymentDto> UnmatchedEShopPayments { get; set; } = new();
}

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range (paging through
/// the whole range) and lines them up against eShop orders, surfacing anything only one side
/// knows about. Note: PayPal's reporting lags live activity, so very recent payments may
/// legitimately be absent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IPayPalGateway _payPalGateway;
    private readonly IReadRepository<Payment> _paymentRepository;

    public ReconciliationEndpoint(IPayPalGateway payPalGateway, IReadRepository<Payment> paymentRepository)
    {
        _payPalGateway = payPalGateway;
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        var from = request.From;
        var to = request.To;
        if (to <= from)
        {
            throw new PaymentValidationException("The 'to' date-time must be after the 'from' date-time (both ISO-8601).");
        }

        var transactions = await _payPalGateway.ListTransactionsAsync(from, to);
        var payments = await _paymentRepository.ListAsync(new PaymentsCreatedInRangeSpecification(from, to));

        var lookup = new Dictionary<string, (Payment payment, string matchedBy)>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            lookup.TryAdd(payment.PayPalOrderId, (payment, "PayPalOrderId"));
            lookup.TryAdd(payment.AuthorizationId, (payment, "AuthorizationId"));
            if (!string.IsNullOrEmpty(payment.CaptureId))
            {
                lookup.TryAdd(payment.CaptureId, (payment, "CaptureId"));
            }
            foreach (var refund in payment.Refunds)
            {
                lookup.TryAdd(refund.PayPalRefundId, (payment, "RefundId"));
            }
        }

        var matchedPaymentIds = new HashSet<int>();
        var response = new ReconciliationResponse { From = from, To = to };

        foreach (var transaction in transactions)
        {
            var dto = new ReconciliationTransactionDto
            {
                TransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                InitiationDate = transaction.InitiationDate
            };

            if (lookup.TryGetValue(transaction.TransactionId, out var match))
            {
                dto.MatchedOrderId = match.payment.OrderId;
                dto.MatchedBy = match.matchedBy;
                matchedPaymentIds.Add(match.payment.Id);
            }
            else
            {
                response.UnmatchedPayPalTransactions.Add(dto);
            }

            response.Transactions.Add(dto);
        }

        response.UnmatchedEShopPayments = payments
            .Where(p => !matchedPaymentIds.Contains(p.Id))
            .Select(p => new ReconciliationEShopPaymentDto
            {
                PaymentId = p.Id,
                OrderId = p.OrderId,
                Status = p.Status.ToString(),
                Amount = p.Amount,
                Currency = p.Currency,
                PayPalOrderId = p.PayPalOrderId,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                RefundIds = p.Refunds.Select(r => r.PayPalRefundId).ToList()
            })
            .ToList();

        response.PayPalTransactionCount = response.Transactions.Count;
        response.MatchedCount = matchedPaymentIds.Count;
        return Results.Ok(response);
    }
}
