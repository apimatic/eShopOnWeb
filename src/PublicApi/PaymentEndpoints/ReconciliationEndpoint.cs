using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationEntryModel
{
    public string State { get; set; } = string.Empty;
    public string? PayPalTransactionId { get; set; }
    public string? InvoiceId { get; set; }
    public decimal? PayPalAmount { get; set; }
    public string? PayPalStatus { get; set; }
    public DateTimeOffset? PayPalDate { get; set; }
    public int? OrderId { get; set; }
    public decimal? OrderAmount { get; set; }
    public string? PaymentStatus { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int PayPalTransactionCount { get; set; }
    public int MatchedCount { get; set; }
    public int MissingInEShopCount { get; set; }
    public int MissingInPayPalCount { get; set; }
    public List<ReconciliationEntryModel> Entries { get; set; } = new();
}

/// <summary>
/// Operator action: a reconciliation report lining PayPal's own transaction record for a date
/// range up against eShop orders, covering the whole range. <c>from</c> and <c>to</c> are
/// ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IReconciliationService reconciliationService, CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest(new
                    {
                        Message = "Both 'from' and 'to' must be supplied as ISO-8601 date-times, e.g. 2026-09-01T00:00:00Z."
                    });
                }

                var report = await reconciliationService.ReconcileAsync(fromDate, toDate, ct);

                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    PayPalTransactionCount = report.PayPalTransactionCount,
                    MatchedCount = report.MatchedCount,
                    MissingInEShopCount = report.MissingInEShopCount,
                    MissingInPayPalCount = report.MissingInPayPalCount,
                    Entries = report.Entries.Select(e => new ReconciliationEntryModel
                    {
                        State = e.State.ToString(),
                        PayPalTransactionId = e.PayPalTransactionId,
                        InvoiceId = e.InvoiceId,
                        PayPalAmount = e.PayPalAmount,
                        PayPalStatus = e.PayPalStatus,
                        PayPalDate = e.PayPalDate,
                        OrderId = e.OrderId,
                        OrderAmount = e.OrderAmount,
                        PaymentStatus = e.PaymentStatus
                    }).ToList()
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
