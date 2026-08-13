using System.Linq;
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
/// POST /api/notifications/{notificationId}/resend — an operator re-sends a message that did not reach the
/// shopper. A caller-supplied idempotency key makes a repeated request under the same key send nothing new,
/// while a fresh key is a legitimate second attempt. Operator action: restricted to the administrator role.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                ResendNotificationRequest? request,
                HttpRequest httpRequest,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                // The idempotency key may arrive in the body or as an Idempotency-Key header.
                var idempotencyKey = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    idempotencyKey = httpRequest.Headers["Idempotency-Key"].FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { message = "An idempotency key is required." });
                }

                var result = await service.ResendAsync(notificationId, idempotencyKey, cancellationToken);
                if (result is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = result.Notification.Id,
                    Reused = result.ReusedExisting,
                    Status = result.Notification.DeliveryStatus
                });
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }
}
