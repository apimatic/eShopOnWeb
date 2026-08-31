using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationRequest : BaseRequest
{
    public GetReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class GetReconciliationResponse : BaseResponse
{
    public GetReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public GetReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<ReconciliationTransactionDto> TransactionsMissingFromEShop { get; set; } = new List<ReconciliationTransactionDto>();
    public List<UnmatchedPaymentDto> PaymentsMissingFromPayPal { get; set; } = new List<UnmatchedPaymentDto>();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? PayPalReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public int? MatchedOrderId { get; set; }
    public string? MatchedEntity { get; set; }
}

public class UnmatchedPaymentDto
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public List<string> RefundIds { get; set; } = new List<string>();
}

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range
/// and lines them up against eShop orders, surfacing entries known to only one
/// side. Covers the whole range (all pages), not just the first page.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest>
{
    private readonly IPaymentService _paymentService;

    public GetReconciliationEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest("Both 'from' and 'to' query parameters (ISO-8601 date-times) are required.");
                }
                return await HandleAsync(new GetReconciliationRequest(from.Value, to.Value));
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request)
    {
        var report = await _paymentService.ReconcileAsync(request.From, request.To);

        var response = new GetReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Transactions = report.Transactions.Select(Map).ToList(),
            TransactionsMissingFromEShop = report.TransactionsMissingFromEShop
                .Select(t => Map(new ReconciliationEntry(t, null, null))).ToList(),
            PaymentsMissingFromPayPal = report.PaymentsMissingFromPayPal.Select(p => new UnmatchedPaymentDto
            {
                OrderId = p.OrderId,
                PayPalOrderId = p.PayPalOrderId,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                RefundIds = p.RefundIds.ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationTransactionDto Map(ReconciliationEntry entry)
    {
        return new ReconciliationTransactionDto
        {
            TransactionId = entry.Transaction.TransactionId,
            PayPalReferenceId = entry.Transaction.PayPalReferenceId,
            EventCode = entry.Transaction.EventCode,
            Status = entry.Transaction.Status,
            Amount = entry.Transaction.Amount,
            Currency = entry.Transaction.Currency,
            Fee = entry.Transaction.Fee,
            InvoiceId = entry.Transaction.InvoiceId,
            InitiationDate = entry.Transaction.InitiationDate,
            MatchedOrderId = entry.MatchedOrderId,
            MatchedEntity = entry.MatchedEntity
        };
    }
}
