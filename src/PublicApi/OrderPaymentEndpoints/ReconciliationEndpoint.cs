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

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

/// <summary>One reconciled row: a PayPal transaction and/or an eShop order, with the match verdict.</summary>
public class ReconciliationEntryDto
{
    public string Match { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public string? CurrencyCode { get; set; }
    public int? OrderId { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? EShopPaymentStatus { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(System.Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalOnlyCount { get; set; }
    public int InEShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, so a payment one side knows about and the other does not is visible.
/// Covers the whole range (all pages). Administrator only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service, CancellationToken ct)
    {
        var report = await service.ReconcileAsync(request.From, request.To, ct);

        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            InPayPalOnlyCount = report.InPayPalOnlyCount,
            InEShopOnlyCount = report.InEShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Match = e.Match.ToString(),
                PayPalTransactionId = e.PayPalTransactionId,
                PayPalAmount = e.PayPalAmount,
                PayPalStatus = e.PayPalStatus,
                CurrencyCode = e.CurrencyCode,
                OrderId = e.OrderId,
                EShopAmount = e.EShopAmount,
                EShopPaymentStatus = e.EShopPaymentStatus
            }).ToList()
        });
    }
}
