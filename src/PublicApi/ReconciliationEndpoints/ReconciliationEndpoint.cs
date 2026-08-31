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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator: lists PayPal's own record of transactions for a date range, lined up
/// against eShop orders — transactions PayPal knows about and eShop doesn't (and the
/// reverse) are visible. Covers the whole range, not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        if (request.From is null || request.To is null)
        {
            return Results.BadRequest("Both 'from' and 'to' (ISO-8601 date-times) are required.");
        }
        if (request.From >= request.To)
        {
            return Results.BadRequest("'from' must be earlier than 'to'.");
        }

        var report = await paymentService.ReconcileAsync(request.From.Value, request.To.Value, CancellationToken.None);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Transactions = report.Transactions.Select(e => new ReconciliationTransactionDto
            {
                TransactionId = e.Transaction.TransactionId,
                ReferenceId = e.Transaction.ReferenceId,
                ReferenceIdType = e.Transaction.ReferenceIdType,
                Status = e.Transaction.Status,
                Amount = e.Transaction.Amount,
                Currency = e.Transaction.Currency,
                Fee = e.Transaction.Fee,
                InvoiceId = e.Transaction.InvoiceId,
                CustomField = e.Transaction.CustomField,
                EventCode = e.Transaction.EventCode,
                Time = e.Transaction.Time,
                OrderId = e.OrderId,
                PaymentId = e.PaymentId,
                MatchedToEShopOrder = e.Matched
            }).ToList(),
            PaymentsMissingInPayPal = report.PaymentsMissingInPayPal.Select(p => new MissingPaymentDto
            {
                PaymentId = p.Id,
                OrderId = p.OrderId,
                CaptureId = p.CaptureId,
                CapturedAmount = p.CapturedAmount,
                Currency = p.Currency,
                CapturedOn = p.CapturedOn
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset? from, DateTimeOffset? to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset? From { get; }
    public DateTimeOffset? To { get; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<MissingPaymentDto> PaymentsMissingInPayPal { get; set; } = new List<MissingPaymentDto>();
}

public class ReconciliationTransactionDto
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public DateTimeOffset? Time { get; set; }
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
    public bool MatchedToEShopOrder { get; set; }
}

public class MissingPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? CapturedOn { get; set; }
}
