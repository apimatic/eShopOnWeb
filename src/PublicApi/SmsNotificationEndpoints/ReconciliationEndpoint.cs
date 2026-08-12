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

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// Operator action: reconcile the provider's own record of messages sent from the configured number over a
/// date range against what eShop believes it sent, so a message either side knows about and the other does
/// not is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times. Administrator-only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderMessagingService service, CancellationToken ct) =>
            {
                if (from > to)
                    return Results.BadRequest("'from' must be earlier than or equal to 'to'.");

                var report = await service.ReconcileAsync(from, to, ct);
                return Results.Ok(report.ToResponse());
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }
}
