using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and lines them up
/// against eShop orders, so a payment PayPal knows about and eShop doesn't — or the reverse — is
/// visible. Covers the whole range. Restricted to the administrator role.
///
/// <para>
/// Note: PayPal's transaction reporting lags live activity, so a range covering payments made
/// moments ago may legitimately return no PayPal rows — that is an expected sandbox result.
/// </para>
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset from,
                DateTimeOffset to,
                IReconciliationService reconciliationService,
                CancellationToken cancellationToken) =>
            {
                var report = await reconciliationService.ReconcileAsync(from, to, cancellationToken);

                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    MatchedCount = report.MatchedCount,
                    InPayPalOnlyCount = report.InPayPalOnlyCount,
                    InEShopOnlyCount = report.InEShopOnlyCount,
                    Entries = report.Entries.Select(e => new ReconciliationEntryView
                    {
                        Outcome = e.Outcome.ToString(),
                        PayPalTransactionId = e.PayPalTransactionId,
                        PayPalStatus = e.PayPalStatus,
                        PayPalAmount = e.PayPalAmount,
                        Currency = e.Currency,
                        PayPalDate = e.PayPalDate,
                        OrderId = e.OrderId,
                        EShopRecordType = e.EShopRecordType,
                        EShopAmount = e.EShopAmount
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int InPayPalOnlyCount { get; set; }
    public int InEShopOnlyCount { get; set; }
    public List<ReconciliationEntryView> Entries { get; set; } = new();
}

public class ReconciliationEntryView
{
    public string Outcome { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? PayPalStatus { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? PayPalDate { get; set; }
    public int? OrderId { get; set; }
    public string? EShopRecordType { get; set; }
    public decimal? EShopAmount { get; set; }
}
