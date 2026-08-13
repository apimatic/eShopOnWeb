using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not reach the
/// shopper. Idempotent on the caller-supplied key. Returns the id of the message the resend produced.
/// Administrator only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest request,
                IOrderMessagingService service,
                CancellationToken cancellationToken) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    throw new BadRequestException("An idempotencyKey is required.");
                }

                var notification = await service.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
                var response = new ResendNotificationResponse
                {
                    NotificationId = notification.Id,
                    Notification = NotificationDto.From(notification)
                };
                return Results.Ok(response);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }
}
