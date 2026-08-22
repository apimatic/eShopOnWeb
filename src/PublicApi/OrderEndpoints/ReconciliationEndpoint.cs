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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset? PayPalLastRefreshed { get; set; }
    public List<ReconciliationRowDto> Rows { get; set; } = new();
}

public class ReconciliationRowDto
{
    public string Match { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalEventCode { get; set; }
    public string? Status { get; set; }
    public decimal? EshopAmount { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public string? Note { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService orders) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, orders);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService orders)
    {
        var report = await orders.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalLastRefreshed = report.PayPalLastRefreshed,
            Rows = report.Rows.Select(r => new ReconciliationRowDto
            {
                Match = r.Match,
                OrderId = r.OrderId,
                PayPalTransactionId = r.PayPalTransactionId,
                PayPalEventCode = r.PayPalEventCode,
                Status = r.Status,
                EshopAmount = r.EshopAmount,
                PayPalAmount = r.PayPalAmount,
                Currency = r.Currency,
                Note = r.Note
            }).ToList()
        });
    }
}
