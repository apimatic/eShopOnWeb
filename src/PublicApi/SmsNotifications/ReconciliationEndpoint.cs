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

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — lists the provider's own record of messages
/// (for the application's configured sending number only) over an ISO-8601 date-time range and lines them up
/// against what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint
    : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service, CancellationToken ct) =>
                await HandleAsync(from, to, service, ct))
            .Produces<ReconciliationReport>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
        HandleAsync(from, to, service, CancellationToken.None);

    private static async Task<IResult> HandleAsync(
        DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service, CancellationToken ct)
    {
        if (from > to)
        {
            return Results.BadRequest(new { error = "'from' must be earlier than or equal to 'to'." });
        }

        var report = await service.ReconcileAsync(from, to, ct);
        return Results.Ok(report);
    }
}
