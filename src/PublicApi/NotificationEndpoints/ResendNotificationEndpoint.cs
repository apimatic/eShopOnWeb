using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an
/// idempotency key (body field or <c>Idempotency-Key</c> header); repeating under the same key does
/// not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext httpContext, INotificationAdminService service, CancellationToken cancellationToken) =>
            {
                var idempotencyKey = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey) &&
                    httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerValue))
                {
                    idempotencyKey = headerValue.ToString();
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { message = "An idempotency key is required (body field 'idempotencyKey' or 'Idempotency-Key' header)." });
                }

                var result = await service.ResendAsync(notificationId, idempotencyKey!, cancellationToken);
                return result.Outcome switch
                {
                    ResendOutcome.Sent or ResendOutcome.Duplicate =>
                        Results.Ok(new ResendNotificationResponse { NotificationId = result.NotificationId!.Value }),
                    ResendOutcome.NotFound => Results.NotFound(),
                    ResendOutcome.ContentDisposed =>
                        Results.Conflict(new { message = "The message content has been disposed of and cannot be re-sent." }),
                    _ => Results.Problem()
                };
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }
}
