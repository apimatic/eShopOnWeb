using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator action. Lists PayPal's own transaction
/// records for the date range and lines them up against eShop orders, covering the whole range.
/// Restricted to administrators. <c>from</c> and <c>to</c> are ISO-8601 date-times.
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
            await PaymentProblem.ExecuteAsync(async () =>
            {
                ReconciliationReport report = await reconciliationService.ReconcileAsync(from, to, cancellationToken);
                return Results.Ok(report);
            }))
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }
}
