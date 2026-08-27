using System.Threading;
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
/// Re-sends a message that did not reach the shopper (operator). The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequestBody body, IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                var request = new ResendNotificationRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = body.IdempotencyKey
                };
                return await HandleAsync(request, notificationService, cancellationToken);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    private async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required.");
        }

        var notification = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey, cancellationToken);

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            OriginalNotificationId = request.NotificationId,
            MessageSid = notification.MessageSid,
            Status = notification.Status
        };
        return Results.Ok(response);
    }
}
