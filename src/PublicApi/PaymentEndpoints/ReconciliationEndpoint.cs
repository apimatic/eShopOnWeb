using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationQuery
{
    public string? From { get; set; }
    public string? To { get; set; }
}

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator action. Lists the provider's own
/// record of transactions for the ISO-8601 date-time range and lines them up against eShop orders,
/// covering the whole range (all pages).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IReconciliationService service) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, service);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IReconciliationService service)
    {
        if (!TryParse(query.From, out var from))
        {
            throw new PaymentOperationException("'from' must be an ISO-8601 date-time (e.g. 2026-09-01T00:00:00Z).");
        }
        if (!TryParse(query.To, out var to))
        {
            throw new PaymentOperationException("'to' must be an ISO-8601 date-time (e.g. 2026-09-30T23:59:59Z).");
        }

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(report);
    }

    private static bool TryParse(string? value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}
