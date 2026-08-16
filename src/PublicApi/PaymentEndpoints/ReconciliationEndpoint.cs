using System;
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
/// GET /api/reconciliation?from={from}&amp;to={to} — lines up PayPal's own transaction records against
/// eShop orders for a date range (whole range, all pages). Administrator role only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
                }

                var report = await reconciliationService.ReconcileAsync(from, to);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }

    // Satisfies IEndpoint; the route delegate above does the work with its query-bound parameters.
    public Task<IResult> HandleAsync(IReconciliationService reconciliationService)
        => Task.FromResult(Results.BadRequest());
}
