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
/// Operator action: line up the provider's own record of messages from this application's
/// configured sending number, over a date range, against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery(Name = "from")] DateTimeOffset from, [FromQuery(Name = "to")] DateTimeOffset to, IOrderNotificationService service) =>
                await HandleAsync(from, to, service))
            .Produces<ReconciliationReport>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(from.ToUniversalTime(), to.ToUniversalTime());
        return Results.Ok(report);
    }
}
