using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEntryDto
{
    public string Match { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? EShopAmount { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public string? OrderStatus { get; set; }
    public string? PayPalStatus { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int EShopCapturedPaymentCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalOnlyCount { get; set; }
    public int InEShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining up PayPal's own transaction
/// record for a date range against eShop orders. Admin only. Covers the whole range (all pages).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service, ClaimsPrincipal user) =>
                await HandleAsync(new ReconciliationRequest { From = from, To = to }, service, user))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service, ClaimsPrincipal user)
    {
        if (request.To < request.From)
        {
            throw new InvalidPaymentRequestException("'to' must be on or after 'from'.");
        }

        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EShopCapturedPaymentCount = report.EShopCapturedPaymentCount,
            MatchedCount = report.MatchedCount,
            InPayPalOnlyCount = report.InPayPalOnlyCount,
            InEShopOnlyCount = report.InEShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Match = e.Match.ToString(),
                OrderId = e.OrderId,
                PayPalTransactionId = e.PayPalTransactionId,
                CaptureId = e.CaptureId,
                EShopAmount = e.EShopAmount,
                PayPalAmount = e.PayPalAmount,
                Currency = e.Currency,
                OrderStatus = e.OrderStatus,
                PayPalStatus = e.PayPalStatus
            }).ToList()
        };
        return Results.Ok(response);
    }
}
