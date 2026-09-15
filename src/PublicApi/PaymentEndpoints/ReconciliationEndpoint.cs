using System;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own transaction
/// records up against eShop orders across the whole date range. Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.Query, IReconciliationService>
{
    public record Query(DateTimeOffset From, DateTimeOffset To);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IReconciliationService service) =>
            {
                if (!TryParseIso(from, out var fromDate))
                {
                    return Results.BadRequest(new { message = "'from' must be an ISO-8601 date-time." });
                }
                if (!TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest(new { message = "'to' must be an ISO-8601 date-time." });
                }
                return await HandleAsync(new Query(fromDate, toDate), service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("Reconciliation");
    }

    public async Task<IResult> HandleAsync(Query request, IReconciliationService service)
    {
        var report = await service.BuildReportAsync(request.From, request.To);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            EShopPaidOrderCount = report.EShopPaidOrderCount,
            MatchedCount = report.Matched.Count,
            InPayPalNotEShopCount = report.InPayPalNotEShop.Count,
            InEShopNotPayPalCount = report.InEShopNotPayPal.Count,
            Matched = Map(report.Matched),
            InPayPalNotEShop = Map(report.InPayPalNotEShop),
            InEShopNotPayPal = Map(report.InEShopNotPayPal)
        };

        return Results.Ok(response);
    }

    private static List<ReconciliationLineDto> Map(IReadOnlyList<ReconciliationLine> lines) =>
        lines.Select(l => new ReconciliationLineDto
        {
            PaymentReference = l.PaymentReference,
            PayPalTransactionId = l.PayPalTransactionId,
            PayPalEventCode = l.PayPalEventCode,
            PayPalStatus = l.PayPalStatus,
            PayPalAmount = l.PayPalAmount,
            Currency = l.Currency,
            PayPalDate = l.PayPalDate,
            OrderId = l.OrderId,
            OrderStatus = l.OrderStatus,
            OrderAmount = l.OrderAmount,
            Note = l.Note
        }).ToList();

    private static bool TryParseIso(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out result);
}
