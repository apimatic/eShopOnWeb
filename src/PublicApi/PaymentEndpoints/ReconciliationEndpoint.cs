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
/// Operator report: PayPal's own record of transactions over a date range, lined up against eShop
/// orders so a payment one side knows about and the other doesn't is visible. Covers the whole
/// range (every page). Restricted to the administrator role.
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
                CancellationToken ct) =>
            {
                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
                }

                var report = await reconciliationService.BuildReportAsync(from, to, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }
}
