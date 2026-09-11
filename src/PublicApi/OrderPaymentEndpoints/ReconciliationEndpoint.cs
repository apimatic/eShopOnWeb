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

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class ReconciliationEntryDto
{
    public string Outcome { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? EventCode { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? TransactionDate { get; set; }
    public string? InvoiceId { get; set; }
    public int? OrderId { get; set; }
    public string? EShopCaptureId { get; set; }
    public decimal? EShopCapturedAmount { get; set; }
    public string? EShopStatus { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalOnlyCount { get; set; }
    public int InEShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// GET /api/reconciliation?from=&amp;to= — operator report lining PayPal's transaction record up against
/// eShop orders across the whole ISO-8601 date range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IReconciliationService _service;

    public ReconciliationEndpoint(IReconciliationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to) => await HandleAsync(from, to))
            .Produces<ReconciliationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var report = await _service.ReconcileAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            InPayPalOnlyCount = report.InPayPalOnlyCount,
            InEShopOnlyCount = report.InEShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Outcome = e.Outcome.ToString(),
                PayPalTransactionId = e.PayPalTransactionId,
                EventCode = e.EventCode,
                PayPalStatus = e.PayPalStatus,
                PayPalAmount = e.PayPalAmount,
                PayPalFee = e.PayPalFee,
                Currency = e.CurrencyCode,
                TransactionDate = e.TransactionDate,
                InvoiceId = e.InvoiceId,
                OrderId = e.OrderId,
                EShopCaptureId = e.EShopCaptureId,
                EShopCapturedAmount = e.EShopCapturedAmount,
                EShopStatus = e.EShopStatus,
            }).ToList(),
        };

        return Results.Ok(response);
    }
}
