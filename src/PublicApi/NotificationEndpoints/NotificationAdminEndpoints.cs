using System;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class NotificationAdminEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await NotificationEndpointResults.ExecuteAsync(async () =>
                {
                    int newNotificationId = await service.ResendAsync(
                        notificationId, request.IdempotencyKey ?? string.Empty, cancellationToken);
                    return Results.Created($"/api/notifications/{newNotificationId}", new { notificationId = newNotificationId });
                }))
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("Notifications");

        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await NotificationEndpointResults.ExecuteAsync(async () =>
                {
                    await service.DisposeContentAsync(notificationId, cancellationToken);
                    return Results.NoContent();
                }))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("Notifications");

        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await NotificationEndpointResults.ExecuteAsync(async () =>
                {
                    ReconciliationDto report = await service.ReconcileAsync(from, to, cancellationToken);
                    return Results.Ok(report);
                }))
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("Notifications");
    }
}

public sealed record ResendNotificationRequest(string? IdempotencyKey);
