using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions for a date range lined up against eShop orders, so a
/// payment one side knows about and the other does not is visible. Covers the whole range (all pages), not just
/// the first. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EShopPaymentCount = report.EShopPaymentCount,
            MatchedCount = report.MatchedCount,
            MissingInEShopCount = report.MissingInEShopCount,
            MissingInPayPalCount = report.MissingInPayPalCount,
            Lines = report.Lines.Select(l => new ReconciliationLineDto
            {
                Match = l.Match.ToString(),
                OrderId = l.OrderId,
                InvoiceId = l.InvoiceId,
                PayPalTransactionId = l.PayPalTransactionId,
                EventCode = l.EventCode,
                PayPalStatus = l.PayPalStatus,
                PayPalAmount = l.PayPalAmount,
                EShopAmount = l.EShopAmount,
                EShopPaymentStatus = l.EShopPaymentStatus,
                Date = l.Date,
                Note = l.Note
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EShopPaymentCount { get; set; }
    public int MatchedCount { get; set; }
    public int MissingInEShopCount { get; set; }
    public int MissingInPayPalCount { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();
}

public class ReconciliationLineDto
{
    public string Match { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? InvoiceId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? EventCode { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? EShopPaymentStatus { get; set; }
    public DateTimeOffset? Date { get; set; }
    public string? Note { get; set; }
}
