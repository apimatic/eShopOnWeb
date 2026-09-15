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

public record ReconciliationLineDto(
    string Match,
    string? InvoiceId,
    int? OrderId,
    string? EShopStatus,
    decimal? EShopAmount,
    string? PayPalTransactionId,
    string? PayPalEventCode,
    decimal? PayPalAmount,
    string? PayPalStatus,
    DateTimeOffset? PayPalDate);

public record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int EShopPaymentCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingInPayPalCount,
    IReadOnlyList<ReconciliationLineDto> Lines);

/// <summary>
/// Operator report lining PayPal's own transaction records up against eShop orders over an ISO-8601 date-time range.
/// Covers the whole range (chunked and fully paged, not just the first page). Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, (DateTimeOffset From, DateTimeOffset To)>
{
    private readonly IReconciliationService _reconciliation;

    public ReconciliationEndpoint(IReconciliationService reconciliation)
    {
        _reconciliation = reconciliation;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest(new { message = "Query parameters 'from' and 'to' are required ISO-8601 date-times, e.g. 2026-08-01T00:00:00Z." });
                }
                return await HandleAsync((fromDate, toDate));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync((DateTimeOffset From, DateTimeOffset To) request)
    {
        var report = await _reconciliation.ReconcileAsync(request.From, request.To);

        var lines = report.Lines.Select(l => new ReconciliationLineDto(
            l.Match.ToString(),
            l.InvoiceId,
            l.OrderId,
            l.EShopStatus?.ToString(),
            l.EShopAmount,
            l.PayPalTransactionId,
            l.PayPalEventCode,
            l.PayPalAmount,
            l.PayPalStatus,
            l.PayPalDate)).ToList();

        return Results.Ok(new ReconciliationResponse(
            report.From, report.To, report.PayPalTransactionCount, report.EShopPaymentCount,
            report.MatchedCount, report.MissingInEShopCount, report.MissingInPayPalCount, lines));
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
    }
}
