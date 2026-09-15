using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class NotificationOperatorEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend", ResendAsync)
            .RequireAuthorization(OperatorPolicy())
            .WithTags("NotificationOperatorEndpoints");

        app.MapDelete("api/notifications/{notificationId:int}/content", DisposeContentAsync)
            .RequireAuthorization(OperatorPolicy())
            .WithTags("NotificationOperatorEndpoints");

        app.MapGet("api/notifications/reconciliation", ReconcileAsync)
            .RequireAuthorization(OperatorPolicy())
            .WithTags("NotificationOperatorEndpoints");
    }

    private static async Task<IResult> ResendAsync(
        int notificationId,
        ResendNotificationRequest request,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var notification = await service.ResendAsync(
                notificationId,
                request.IdempotencyKey,
                cancellationToken);
            return Results.Ok(new { notificationId = notification.Id });
        }
        catch (Exception exception)
        {
            return EndpointProblem.From(exception);
        }
    }

    private static async Task<IResult> DisposeContentAsync(
        int notificationId,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DisposeNotificationContentAsync(notificationId, cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception)
        {
            return EndpointProblem.From(exception);
        }
    }

    private static async Task<IResult> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        OrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.ReconcileAsync(from, to, cancellationToken));
        }
        catch (Exception exception)
        {
            return EndpointProblem.From(exception);
        }
    }

    private static AuthorizeAttribute OperatorPolicy() => new()
    {
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme,
        Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS
    };
}

public sealed record ResendNotificationRequest(string IdempotencyKey);
