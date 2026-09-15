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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();
}

public class ReconciliationLineDto
{
    public string MatchState { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
    public string? EventCode { get; set; }
    public string? TransactionStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public string? InvoiceId { get; set; }
    public int? OrderId { get; set; }
    public string? OrderPaymentStatus { get; set; }
    public decimal? OrderAmount { get; set; }
    public DateTimeOffset? Date { get; set; }
}

/// <summary>
/// Operator report: PayPal's own transaction record for a date range, lined up against eShop
/// orders. Covers the whole range (chunked and fully paged). from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IReconciliationService service) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { message = "'from' and 'to' ISO-8601 date-times are required." });
                }
                return await HandleAsync(new ReconciliationRequest { From = from.Value, To = to.Value }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.BuildAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Lines = report.Lines.Select(l => new ReconciliationLineDto
            {
                MatchState = l.MatchState,
                TransactionId = l.TransactionId,
                EventCode = l.EventCode,
                TransactionStatus = l.TransactionStatus,
                PayPalAmount = l.PayPalAmount,
                Currency = l.Currency,
                Fee = l.Fee,
                InvoiceId = l.InvoiceId,
                OrderId = l.OrderId,
                OrderPaymentStatus = l.OrderPaymentStatus,
                OrderAmount = l.OrderAmount,
                Date = l.Date
            }).ToList()
        };
        return Results.Ok(response);
    }
}
