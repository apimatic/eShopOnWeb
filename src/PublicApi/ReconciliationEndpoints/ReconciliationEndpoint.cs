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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator report: PayPal's own transactions for a date range, lined up against eShop's records so
/// either-side-only discrepancies are visible. Covers the whole range (follows PayPal's pagination).
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Reconcile PayPal transactions against eShop orders (operator)", Tags = new[] { "ReconciliationEndpoints" })]
            async (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService, CancellationToken ct) =>
            {
                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
                }
                var report = await reconciliationService.BuildAsync(from, to, ct);
                return Results.Ok(report);
            })
            .Produces<ApplicationCore.Payments.ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }
}
