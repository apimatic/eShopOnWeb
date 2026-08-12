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

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — operator report. Lists the
/// provider's own record of messages sent from this application's configured sending number
/// over the range and lines them up against what eShop believes it sent. from/to are ISO-8601.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                if (from is null || to is null)
                    return Results.BadRequest(new { message = "Both 'from' and 'to' (ISO-8601 date-times) are required." });

                if (from > to)
                    return Results.BadRequest(new { message = "'from' must not be later than 'to'." });

                var report = await notificationService.ReconcileAsync(from.Value, to.Value, cancellationToken);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }
}
