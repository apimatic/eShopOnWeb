using System;
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
/// Operator action: lines up the provider's own record of messages sent from this
/// application's sending number against what eShop believes it sent, over a date range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IOrderNotificationService _notificationService;

    public ReconciliationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationReport>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });
        }

        var report = await _notificationService.ReconcileAsync(from.ToUniversalTime(), to.ToUniversalTime());
        return Results.Ok(report);
    }
}
