using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: reconciles PayPal's own transaction records against eShop orders for a date range,
/// covering the whole range. Restricted to the administrator role. <c>from</c>/<c>to</c> are ISO-8601.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset from,
                DateTimeOffset to,
                IReconciliationService service,
                CancellationToken ct) =>
            {
                if (to < from)
                {
                    return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
                }

                var report = await service.ReconcileAsync(from, to, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }
}
