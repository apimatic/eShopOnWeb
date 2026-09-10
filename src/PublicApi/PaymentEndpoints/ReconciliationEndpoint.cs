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

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalNotInEShopCount { get; set; }
    public int InEShopNotInPayPalCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string Category { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public string? EventCode { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? PayPalDate { get; set; }
    public int? EShopOrderId { get; set; }
    public string? EShopCaptureId { get; set; }
    public string? EShopOrderStatus { get; set; }
}

/// <summary>
/// Operator action: reconciles PayPal's own record of transactions for a date range against eShop
/// orders, so a payment PayPal knows about that eShop does not — or the reverse — is visible. Covers
/// the whole range (all pages). <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service) => await HandleAsync(from, to, service))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service)
    {
        var report = await service.ReconcileAsync(from, to);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            InPayPalNotInEShopCount = report.InPayPalNotInEShopCount,
            InEShopNotInPayPalCount = report.InEShopNotInPayPalCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Category = e.Category,
                PayPalTransactionId = e.PayPalTransactionId,
                PayPalStatus = e.PayPalStatus,
                EventCode = e.EventCode,
                PayPalAmount = e.PayPalAmount,
                Currency = e.Currency,
                PayPalDate = e.PayPalDate,
                EShopOrderId = e.EShopOrderId,
                EShopCaptureId = e.EShopCaptureId,
                EShopOrderStatus = e.EShopOrderStatus?.ToString()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
