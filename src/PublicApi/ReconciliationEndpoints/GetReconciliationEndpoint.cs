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

/// <summary>
/// Operator action: lists PayPal's own record of transactions over a date range
/// (all pages, whole range) and lines them up against eShop orders, so a
/// transaction only one side knows about is visible.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPayPalClient>
{
    private readonly IRepository<Payment> _paymentRepository;

    public GetReconciliationEndpoint(IRepository<Payment> paymentRepository)
    {
        _paymentRepository = paymentRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IPayPalClient payPalClient) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, payPalClient);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPayPalClient payPalClient)
    {
        var response = new ReconciliationResponse(request.CorrelationId());

        if (request.From is null || request.To is null)
        {
            return Results.BadRequest("Both from and to (ISO-8601 date-times) are required.");
        }
        if (request.From >= request.To)
        {
            return Results.BadRequest("from must be earlier than to.");
        }

        var payPalTransactions = await payPalClient.ListTransactionsAsync(request.From.Value, request.To.Value);
        var payments = await _paymentRepository.ListAsync(new PaymentsInRangeSpec(request.From.Value, request.To.Value));

        var entries = new List<ReconciliationEntryDto>();
        var matchedPaymentIds = new HashSet<int>();

        foreach (var transaction in payPalTransactions)
        {
            var match = payments.FirstOrDefault(p =>
                p.PayPalOrderId == transaction.TransactionId
                || p.AuthorizationId == transaction.TransactionId
                || p.CaptureId == transaction.TransactionId
                || p.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId));

            if (match is not null)
            {
                matchedPaymentIds.Add(match.Id);
            }

            entries.Add(new ReconciliationEntryDto
            {
                TransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                Status = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Time = transaction.InitiationDate,
                MatchedOrderId = match?.OrderId,
                MatchedPaymentId = match?.Id,
                MatchStatus = match is not null ? "Matched" : "OnlyInPayPal"
            });
        }

        foreach (var payment in payments.Where(p => !matchedPaymentIds.Contains(p.Id)))
        {
            entries.Add(new ReconciliationEntryDto
            {
                TransactionId = payment.CaptureId ?? payment.AuthorizationId,
                Status = payment.CaptureStatus ?? payment.AuthorizationStatus,
                Amount = payment.CapturedAmount ?? payment.AuthorizedAmount,
                Currency = payment.Currency,
                Time = payment.CapturedAt ?? payment.CreatedAt,
                MatchedOrderId = payment.OrderId,
                MatchedPaymentId = payment.Id,
                MatchStatus = "OnlyInEShop"
            });
        }

        response.From = request.From.Value;
        response.To = request.To.Value;
        response.Entries = entries.OrderBy(e => e.Time).ToList();
        response.TotalPayPalTransactions = payPalTransactions.Count;
        response.MatchedCount = entries.Count(e => e.MatchStatus == "Matched");
        response.OnlyInPayPalCount = entries.Count(e => e.MatchStatus == "OnlyInPayPal");
        response.OnlyInEShopCount = entries.Count(e => e.MatchStatus == "OnlyInEShop");
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInPayPalCount { get; set; }
    public int OnlyInEShopCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? Time { get; set; }
    public int? MatchedOrderId { get; set; }
    public int? MatchedPaymentId { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
}
