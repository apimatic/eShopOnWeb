using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: the provider's own record of messages from this application's configured sending number
/// over a date range, lined up against what eShop believes it sent, so a message either side knows about and
/// the other does not is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery(Name = "from")] DateTimeOffset from, [FromQuery(Name = "to")] DateTimeOffset to, IOrderNotificationService service) =>
            {
                if (to < from)
                    return Results.BadRequest(new { error = "'to' must be on or after 'from'." });

                var report = await service.ReconcileAsync(from, to);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .ProducesValidationProblem()
            .WithTags("NotificationEndpoints");
    }

    // Convention member; the route work runs in the lambda above.
    public Task<IResult> HandleAsync(IOrderNotificationService service) =>
        Task.FromResult(Results.Ok());
}
