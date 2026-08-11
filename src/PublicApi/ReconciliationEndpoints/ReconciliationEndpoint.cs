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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Reconciliation report for a date range: PayPal's own record of transactions lined up against
/// eShop orders, so a payment one side knows about and the other does not is visible. Covers the
/// whole range (all pages). Operator action (administrator role). <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService, CancellationToken ct) =>
            {
                var report = await paymentService.ReconcileAsync(from, to, ct);
                return Results.Ok(ReconciliationResponse.Create(report));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryView> Entries { get; set; } = new();

    public static ReconciliationResponse Create(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        PayPalTransactionCount = report.PayPalTransactionCount,
        MatchedCount = report.MatchedCount,
        PayPalOnlyCount = report.PayPalOnlyCount,
        EShopOnlyCount = report.EShopOnlyCount,
        Entries = report.Entries.Select(e => new ReconciliationEntryView
        {
            PayPalTransactionId = e.PayPalTransactionId,
            InvoiceId = e.InvoiceId,
            OrderId = e.OrderId,
            PayPalAmount = e.PayPalAmount,
            EShopAmount = e.EShopAmount,
            Currency = e.CurrencyCode,
            PayPalStatus = e.PayPalStatus,
            MatchStatus = e.MatchStatus,
            TransactionDate = e.TransactionDate
        }).ToList()
    };
}

public class ReconciliationEntryView
{
    public string? PayPalTransactionId { get; set; }
    public string? InvoiceId { get; set; }
    public int? OrderId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public decimal? EShopAmount { get; set; }
    public string Currency { get; set; } = default!;
    public string? PayPalStatus { get; set; }
    /// <summary>One of <c>Matched</c>, <c>PayPalOnly</c>, <c>EShopOnly</c>.</summary>
    public string MatchStatus { get; set; } = default!;
    public DateTimeOffset? TransactionDate { get; set; }
}
