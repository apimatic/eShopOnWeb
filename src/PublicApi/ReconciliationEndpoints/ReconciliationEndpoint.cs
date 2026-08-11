using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator action. Lists PayPal's own record of
/// transactions for the date range and lines them up against eShop orders, covering the whole
/// range (chunked and paged), so a mismatch either way is visible. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IReconciliationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IReconciliationService service, HttpContext ctx) =>
                await HandleAsync(service, ctx))
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(IReconciliationService service, HttpContext ctx)
    {
        var from = ParseRequired(ctx, "from");
        var to = ParseRequired(ctx, "to");

        var report = await service.ReconcileAsync(from, to, ctx.RequestAborted);
        return Results.Ok(report);
    }

    private static DateTimeOffset ParseRequired(HttpContext ctx, string key)
    {
        var raw = ctx.Request.Query[key].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new PaymentValidationException($"Query parameter '{key}' is required (ISO-8601 date-time).");
        }
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            throw new PaymentValidationException($"Query parameter '{key}' is not a valid ISO-8601 date-time.");
        }
        return value;
    }
}
