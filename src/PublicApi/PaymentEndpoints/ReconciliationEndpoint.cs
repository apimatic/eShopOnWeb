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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own transaction
/// record for the range up against eShop orders. Covers the whole range (all pages), not just the first.
/// from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                IReconciliationService reconciliationService,
                CancellationToken cancellationToken) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                }
                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
                }

                var report = await reconciliationService.BuildAsync(from.Value, to.Value, cancellationToken);
                return Results.Ok(report);
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("PaymentEndpoints");
    }
}
