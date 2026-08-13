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

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

public class ResendNotificationRequest
{
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not reach
/// the shopper. Idempotent on a caller-supplied key (header "Idempotency-Key" or body). Returns the
/// notificationId of the message the resend produced. Administrator only.
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
                // The idempotency key may come from the standard header or the request body.
                var headerKey = httpRequest.Headers["Idempotency-Key"].ToString();
                var idempotencyKey = !string.IsNullOrWhiteSpace(headerKey) ? headerKey : request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.BadRequest(new { error = "An idempotency key is required (header 'Idempotency-Key' or body 'idempotencyKey')." });
                }

                var result = await service.ResendAsync(notificationId, idempotencyKey, cancellationToken);
                var n = result.Notification;
                return result.Outcome switch
                {
                    ResendOutcome.Sent => Results.Created(
                        $"api/notifications/{n!.Id}",
                        new ResendNotificationResponse(n.Id, result.Outcome.ToString(), n.DeliveryStatus, n.ProviderMessageSid)),
                    // Repeating the same key returns the earlier result without sending again.
                    ResendOutcome.Duplicate => Results.Ok(
                        new ResendNotificationResponse(n!.Id, result.Outcome.ToString(), n.DeliveryStatus, n.ProviderMessageSid)),
                    ResendOutcome.AlreadyDelivered => Results.Conflict(new { error = "The message already reached the shopper; nothing to re-send." }),
                    ResendOutcome.ContentDisposed => Results.Conflict(new { error = "The message content has been disposed of and cannot be re-sent." }),
                    _ => Results.NotFound()
                };
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces<ResendNotificationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }
}
