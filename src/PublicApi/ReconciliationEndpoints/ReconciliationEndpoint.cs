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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: lists PayPal's own record of transactions for a date range and lines
/// them up against eShop orders, so a payment PayPal knows about and eShop doesn't — or the
/// reverse — is visible. Covers the whole range (windowed and fully paged).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery(Name = "from")] DateTimeOffset from, [FromQuery(Name = "to")] DateTimeOffset to,
             IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.BuildReportAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Currency = report.Currency,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            InPayPalNotInEShopCount = report.InPayPalNotInEShopCount,
            InEShopNotInPayPalCount = report.InEShopNotInPayPalCount,
            Rows = report.Rows.Select(r => new ReconciliationRowDto
            {
                PayPalTransactionId = r.PayPalTransactionId,
                PayPalStatus = r.PayPalStatus,
                PayPalAmount = r.PayPalAmount,
                PayPalFee = r.PayPalFee,
                PayPalDate = r.PayPalDate,
                OrderId = r.OrderId,
                OrderPaymentStatus = r.OrderPaymentStatus,
                OrderTotal = r.OrderTotal,
                MatchState = r.MatchState
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(System.Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalNotInEShopCount { get; set; }
    public int InEShopNotInPayPalCount { get; set; }
    public List<ReconciliationRowDto> Rows { get; set; } = new();
}

public class ReconciliationRowDto
{
    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public DateTimeOffset? PayPalDate { get; set; }
    public int? OrderId { get; set; }
    public string? OrderPaymentStatus { get; set; }
    public decimal? OrderTotal { get; set; }
    public string MatchState { get; set; } = string.Empty;
}
