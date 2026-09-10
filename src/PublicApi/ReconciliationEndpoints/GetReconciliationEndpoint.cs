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
/// GET /api/reconciliation?from={from}&amp;to={to} — lists PayPal's own transaction record for the
/// date range and lines it up against eShop orders. Covers the whole range. Administrator-only.
/// <paramref name="from"/> and <paramref name="to"/> are ISO-8601 date-times.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IReconciliationService reconciliationService, CancellationToken ct) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                }
                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
                }

                var report = await reconciliationService.ReconcileAsync(from.Value, to.Value, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }
}
