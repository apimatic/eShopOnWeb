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
/// Operator action: a report over a date range lining up the provider's own record of messages sent from this
/// application's configured sending number against what eShop believes it sent, so a message one side knows
/// about and the other does not is visible. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                IOrderNotificationService notifications,
                CancellationToken ct) =>
            {
                if (from is null || to is null)
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                if (from > to)
                    return Results.BadRequest(new { message = "'from' must not be after 'to'." });

                try
                {
                    var report = await notifications.ReconcileAsync(from.Value, to.Value, ct);
                    return Results.Ok(report);
                }
                catch (SmsGatewayException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<ReconciliationReport>()
            .WithTags("NotificationEndpoints");
    }
}
