using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator action. Lists PayPal's own record of
/// transactions for the date range and lines them up against eShop orders, so a payment one side
/// knows about and the other does not is visible. Covers the whole range, not just the first page.
/// Restricted to administrators. from/to are ISO-8601 date-times.
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
                return Results.Ok(report);
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("ReconciliationEndpoints");
    }
}
