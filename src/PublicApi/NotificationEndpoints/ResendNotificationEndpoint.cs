using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                request.NotificationId = notificationId;
                request.CancellationToken = cancellationToken;
                return await HandleAsync(request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new ResendNotificationResponse(request.CorrelationId())
            {
                Error = "idempotencyKey is required."
            });
        }

        var result = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey, request.CancellationToken);

        return result.Status switch
        {
            ResendNotificationStatus.NotificationNotFound => Results.NotFound(),
            ResendNotificationStatus.ContentDisposed => Results.Conflict(new ResendNotificationResponse(request.CorrelationId())
            {
                Error = "The message content has been disposed of and can no longer be sent."
            }),
            ResendNotificationStatus.IdempotencyKeyConflict => Results.Conflict(new ResendNotificationResponse(request.CorrelationId())
            {
                Error = "This idempotency key was already used for a different notification."
            }),
            ResendNotificationStatus.Duplicate => Results.Ok(ToResponse(request, result.Notification!, duplicate: true)),
            _ => Results.Ok(ToResponse(request, result.Notification!, duplicate: false))
        };
    }

    private static ResendNotificationResponse ToResponse(ResendNotificationRequest request, OrderNotification notification, bool duplicate) =>
        new(request.CorrelationId())
        {
            NotificationId = notification.Id,
            ResendOfNotificationId = notification.ResendOfNotificationId,
            Status = notification.Status,
            ProviderMessageSid = notification.ProviderMessageSid,
            Duplicate = duplicate
        };
}
