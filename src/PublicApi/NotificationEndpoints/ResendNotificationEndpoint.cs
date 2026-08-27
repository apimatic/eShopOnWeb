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
/// Operator action: re-sends a message that did not reach the shopper. Idempotent on the
/// caller-supplied key: repeating under the same key does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, request, notificationService);
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces<ResendNotificationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "idempotencyKey is required" });
        }

        var result = await notificationService.ResendAsync(notificationId, request.IdempotencyKey);

        return result.Outcome switch
        {
            ResendOutcome.NotFound => Results.NotFound(),
            ResendOutcome.ContentDisposed => Results.Conflict(new { error = "The message content has been disposed of and can no longer be sent" }),
            ResendOutcome.ContactNumberRemoved => Results.Conflict(new { error = "The contact number was removed and must not be messaged again" }),
            ResendOutcome.AlreadyProcessed => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status,
                AlreadyProcessed = true
            }),
            _ => Results.Created($"api/notifications/{result.Notification!.Id}", new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification.Id,
                Status = result.Notification.Status,
                AlreadyProcessed = false
            })
        };
    }
}
