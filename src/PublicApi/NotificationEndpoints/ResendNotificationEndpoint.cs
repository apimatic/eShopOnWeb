using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public record ResendNotificationRequest(int NotificationId, string IdempotencyKey);

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator action. Re-sends a message that did not reach
/// the shopper. Idempotent on the caller-supplied key. Returns the notificationId the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequestBody body, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, body?.IdempotencyKey ?? string.Empty), service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotencyKey is required." });
        }

        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        return result.Outcome switch
        {
            ResendOutcome.Sent => Results.Ok(new ResendNotificationResponse
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.SendFailed ? "not_sent" : (result.Notification.ProviderStatus ?? "queued"),
                Message = "Resend processed."
            }),
            ResendOutcome.AlreadyProcessed => Results.Ok(new ResendNotificationResponse
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.SendFailed ? "not_sent" : (result.Notification.ProviderStatus ?? "queued"),
                Message = "This idempotency key was already processed; no second message was sent."
            }),
            ResendOutcome.NotificationNotFound => Results.NotFound(new { error = "Notification not found." }),
            ResendOutcome.DestinationRemoved => Results.Conflict(new { error = "The destination number has been removed; nothing may be sent to it again." }),
            ResendOutcome.ContentDisposed => Results.Conflict(new { error = "The message content has been disposed of; there is nothing to re-send." }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
