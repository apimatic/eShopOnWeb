using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: reports the provider's own record of messages for a date range, lined up against
/// what eShop believes it sent, so discrepancies either way are visible. Counts only messages from the
/// configured sending number (Twilio:FromNumber). Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(from, to, service);
            })
            .Produces<ReconciliationReport>()
            .ProducesValidationProblem()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service)
    {
        if (to < from)
            return Results.ValidationProblem(new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["to"] = new[] { "'to' must be on or after 'from'." }
            });

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(report);
    }
}
