using System;
using System.Globalization;
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

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator action. Lists PayPal's own record of
/// transactions for the range and lines them up against eShop orders, over the whole range.
/// from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, HttpContext>
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
            (HttpContext http, CancellationToken ct) => await HandleAsync(http))
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        if (!TryParseIso(http.Request.Query["from"], out var from))
        {
            return Results.BadRequest("A valid ISO-8601 'from' date-time is required.");
        }
        if (!TryParseIso(http.Request.Query["to"], out var to))
        {
            return Results.BadRequest("A valid ISO-8601 'to' date-time is required.");
        }
        if (to < from)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        var report = await _reconciliation.ReconcileAsync(from, to, http.RequestAborted);
        return Results.Ok(report);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
