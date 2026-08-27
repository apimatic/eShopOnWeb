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
/// Operator action: lines the provider's own record of messages for a date range
/// up against what eShop believes it sent. Only messages from this application's
/// configured sending number are counted.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly INotificationService _notificationService;

    public ReconciliationEndpoint(INotificationService notificationService)
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
        if (from == default || to == default || to < from)
        {
            return Results.BadRequest(new { message = "Query parameters 'from' and 'to' must be ISO-8601 date-times with 'to' on or after 'from'." });
        }

        var report = await _notificationService.ReconcileAsync(from.ToUniversalTime(), to.ToUniversalTime());
        return Results.Ok(report);
    }
}
