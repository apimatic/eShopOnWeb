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

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationLineDto
{
    public string Source { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTimeOffset? Date { get; set; }
    public int? OrderId { get; set; }
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

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — lists PayPal's own transactions for the range and
/// lines them up against eShop orders. Covers the whole range (paginated). Administrator only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
                await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliationService))
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService)
    {
        var report = await reconciliationService.ReconcileAsync(request.From, request.To);

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Lines = report.Lines.Select(l => new ReconciliationLineDto
            {
                Source = l.Source,
                PayPalTransactionId = l.PayPalTransactionId,
                Status = l.Status,
                Amount = l.Amount,
                CurrencyCode = l.CurrencyCode,
                Date = l.Date,
                OrderId = l.OrderId
            }).ToList()
        });
    }
}
