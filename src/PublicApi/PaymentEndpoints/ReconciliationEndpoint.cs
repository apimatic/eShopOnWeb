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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationLineDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public decimal? EShopAmount { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public bool? AmountMatches { get; set; }
    public DateTimeOffset? Date { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(System.Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalNotInEShopCount { get; set; }
    public int InEShopNotInPayPalCount { get; set; }
    public List<ReconciliationLineDto> Matched { get; set; } = new();
    public List<ReconciliationLineDto> InPayPalNotInEShop { get; set; } = new();
    public List<ReconciliationLineDto> InEShopNotInPayPal { get; set; } = new();
}

/// <summary>
/// Operator report: PayPal's transaction record over a date range lined up against eShop
/// orders, so a payment one side knows about and the other doesn't is visible. Admin-only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            InPayPalNotInEShopCount = report.InPayPalNotInEShop.Count,
            InEShopNotInPayPalCount = report.InEShopNotInPayPal.Count,
            Matched = report.Matched.Select(m => new ReconciliationLineDto
            {
                TransactionId = m.TransactionId,
                Kind = m.Kind,
                OrderId = m.OrderId,
                EShopAmount = m.EShopAmount,
                PayPalAmount = m.PayPalAmount,
                PayPalStatus = m.PayPalStatus,
                AmountMatches = m.AmountMatches
            }).ToList(),
            InPayPalNotInEShop = report.InPayPalNotInEShop.Select(t => new ReconciliationLineDto
            {
                TransactionId = t.TransactionId,
                Kind = "paypal",
                PayPalAmount = t.Amount,
                PayPalStatus = t.Status,
                Date = t.Date
            }).ToList(),
            InEShopNotInPayPal = report.InEShopNotInPayPal.Select(t => new ReconciliationLineDto
            {
                TransactionId = t.TransactionId,
                Kind = t.Kind,
                OrderId = t.OrderId,
                EShopAmount = t.Amount,
                PayPalStatus = t.Status,
                Date = t.Date
            }).ToList()
        };
        return Results.Ok(response);
    }
}
