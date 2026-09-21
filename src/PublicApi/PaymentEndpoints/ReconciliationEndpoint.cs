using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator report lining PayPal's transactions up against eShop orders over an ISO-8601 date-time
/// range. Covers the whole range (every page), not just the first.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                [FromQuery(Name = "from")] DateTimeOffset from,
                [FromQuery(Name = "to")] DateTimeOffset to,
                IReconciliationService service,
                CancellationToken ct) =>
            {
                var report = await service.ReconcileAsync(from, to, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }
}
