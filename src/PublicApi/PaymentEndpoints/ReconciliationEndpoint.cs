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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEntryDto
{
    public string Match { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? EShopReference { get; set; }
    public int? OrderId { get; set; }
    public string ReferenceKind { get; set; } = string.Empty;
    public decimal? PayPalAmount { get; set; }
    public decimal? EShopAmount { get; set; }
    public string? Status { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: reconcile PayPal's own transactions for a date range against eShop orders, so a
/// payment one side knows about and the other does not is visible. Covers the whole range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService service, CancellationToken ct)
    {
        var report = await service.ReconcileAsync(request.From, request.To, ct);

        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(Map).ToList()
        });
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry e) => new()
    {
        Match = e.Match.ToString(),
        PayPalTransactionId = e.PayPalTransactionId,
        EShopReference = e.EShopReference,
        OrderId = e.OrderId,
        ReferenceKind = e.ReferenceKind,
        PayPalAmount = e.PayPalAmount,
        EShopAmount = e.EShopAmount,
        Status = e.Status
    };
}
