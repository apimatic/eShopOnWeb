using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. The caller-supplied idempotency key
/// makes a repeat under the same key a no-op (no second message); a fresh key is a genuine new attempt.
/// Returns the notificationId of the message the resend produced. Administrator-only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderMessagingService service, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request?.IdempotencyKey))
                    return Results.BadRequest("An idempotency key is required.");

                var notification = await service.ResendAsync(notificationId, request.IdempotencyKey, ct);
                if (notification is null)
                    return Results.NotFound();

                return Results.Ok(new ResendNotificationResponse(notification.Id, notification.Status));
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}
