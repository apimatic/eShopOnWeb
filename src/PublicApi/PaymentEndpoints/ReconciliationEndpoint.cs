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

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public CancellationToken CancellationToken { get; set; }
}

public record ReconciliationEntryDto(
    string Match,
    string? PayPalTransactionId,
    string? OrderReference,
    int? EShopOrderId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? Currency,
    string? Status,
    DateTimeOffset? TransactionDate);

public record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int InPayPalOnlyCount,
    int InEShopOnlyCount,
    bool PayPalDataComplete,
    IReadOnlyList<ReconciliationEntryDto> Entries);

/// <summary>
/// GET /api/reconciliation?from=&amp;to= — operator action: PayPal's transactions over the range
/// lined up against eShop orders (both directions). Covers the whole range. Admin-only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IReconciliationService service, HttpContext ctx) =>
            {
                if (!TryParseIso(from, out var fromDt) || !TryParseIso(to, out var toDt))
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
                if (toDt < fromDt)
                    return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });

                return await HandleAsync(new ReconciliationRequest
                {
                    From = fromDt,
                    To = toDt,
                    CancellationToken = ctx.RequestAborted
                }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, request.CancellationToken);
        var entries = report.Entries.Select(e => new ReconciliationEntryDto(
            e.Match.ToString(), e.PayPalTransactionId, e.OrderReference, e.EShopOrderId,
            e.PayPalAmount, e.EShopAmount, e.Currency, e.Status, e.TransactionDate)).ToList();

        return Results.Ok(new ReconciliationResponse(
            report.From, report.To, report.MatchedCount, report.InPayPalOnlyCount,
            report.InEShopOnlyCount, report.PayPalDataComplete, entries));
    }

    private static bool TryParseIso(string value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out result);
}
