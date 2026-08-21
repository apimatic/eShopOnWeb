using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: reconciles PayPal's own transaction records against eShop orders for a date range,
/// covering the whole range (all pages). Restricted to administrators. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service) =>
            {
                if (to < from)
                    return Results.BadRequest(new { error = "'to' must be on or after 'from'." });

                var report = await service.ReconcileAsync(from, to);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderPaymentService service) =>
        Task.FromResult<IResult>(Results.Ok());
}
