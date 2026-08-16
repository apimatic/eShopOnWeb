using System;
using System.Collections.Generic;
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

// ---------- Response DTOs ----------

public class ReconciliationLineDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();
}

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, so a payment one side knows about and the other does not is visible. Covers
/// the whole range (every page of PayPal's report), not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService service)
        => HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var report = await service.ReconcileAsync(request.From, request.To, ct);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            CurrencyCode = report.CurrencyCode,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
        };
        foreach (var line in report.Lines)
        {
            response.Lines.Add(new ReconciliationLineDto
            {
                TransactionId = line.TransactionId,
                State = line.State.ToString(),
                OrderId = line.OrderId,
                PayPalAmount = line.PayPalAmount,
                EShopAmount = line.EShopAmount,
                PayPalStatus = line.PayPalStatus,
                Kind = line.Kind,
                CurrencyCode = line.CurrencyCode,
            });
        }
        return Results.Ok(response);
    }
}

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}
